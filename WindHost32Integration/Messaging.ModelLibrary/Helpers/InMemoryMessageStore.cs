using System.Collections.Concurrent;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Helpers;

/// <summary>
/// In-memory implementation of IMessageStore for development and testing
/// </summary>
public class InMemoryMessageStore : IMessageStore, IDisposable
{
    #region Fields
    private readonly ConcurrentDictionary<string, Message> _messages = new();
    private readonly ConcurrentDictionary<string, List<Message>> _conversationCache = new();
    private readonly ConcurrentDictionary<string, List<Message>> _userMessagesCache = new();
    private readonly object _cacheLock = new();
    private readonly Timer _cleanupTimer;
    private readonly TimeSpan _messageRetention;
    private bool _disposed;
    #endregion

    #region Constructor
    public InMemoryMessageStore(TimeSpan? messageRetention = null)
    {
        _messageRetention = messageRetention ?? TimeSpan.FromDays(30);
        _cleanupTimer = new Timer(CleanupExpiredMessages, null,
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }
    #endregion

    #region IMessageStore Implementation
    public async Task<bool> StoreMessageAsync(Message message)
    {
        try
        {
            _messages[message.Id] = message;

            lock (_cacheLock)
            {
                // Update user messages cache
                var senderKey = message.Sender;
                if (!_userMessagesCache.TryGetValue(senderKey, out var senderMessages))
                {
                    senderMessages = new List<Message>();
                    _userMessagesCache[senderKey] = senderMessages;
                }
                senderMessages.Add(message);

                // Update conversation cache
                if (!string.IsNullOrEmpty(message.Receiver) && message.Receiver != "*")
                {
                    var conversationKey = GetConversationKey(message.Sender, message.Receiver);
                    if (!_conversationCache.TryGetValue(conversationKey, out var conversation))
                    {
                        conversation = new List<Message>();
                        _conversationCache[conversationKey] = conversation;
                    }
                    conversation.Add(message);
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(string senderId, string receiverId, int limit = 100)
    {
        try
        {
            var messages = _messages.Values
                .Where(m => (m.Sender == senderId && m.Receiver == receiverId) ||
                            (m.Sender == receiverId && m.Receiver == senderId))
                .OrderBy(m => m.Timestamp)
                .TakeLast(limit)
                .ToList();

            return messages;
        }
        catch (Exception)
        {
            return new List<Message>();
        }
    }

    public async Task<IReadOnlyList<Message>> GetConversationAsync(string clientId1, string clientId2, int limit = 100)
    {
        try
        {
            var conversationKey = GetConversationKey(clientId1, clientId2);

            lock (_cacheLock)
            {
                if (_conversationCache.TryGetValue(conversationKey, out var conversation))
                {
                    return conversation.TakeLast(limit).ToList();
                }
            }

            return new List<Message>();
        }
        catch (Exception)
        {
            return new List<Message>();
        }
    }

    public async Task<Message?> GetMessageAsync(string messageId)
    {
        try
        {
            _messages.TryGetValue(messageId, out var message);
            return message;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> MarkMessageAsDeliveredAsync(string messageId)
    {
        try
        {
            if (_messages.TryGetValue(messageId, out var message))
            {
                message.Metadata["DeliveredAt"] = DateTime.UtcNow;
                message.Metadata["Status"] = "Delivered";
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> MarkMessageAsReadAsync(string messageId)
    {
        try
        {
            if (_messages.TryGetValue(messageId, out var message))
            {
                message.Metadata["ReadAt"] = DateTime.UtcNow;
                message.Metadata["Status"] = "Read";
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteMessageAsync(string messageId)
    {
        try
        {
            if (_messages.TryRemove(messageId, out var message))
            {
                lock (_cacheLock)
                {
                    // Remove from caches
                    foreach (var cache in _conversationCache.Values)
                    {
                        cache.RemoveAll(m => m.Id == messageId);
                    }

                    foreach (var cache in _userMessagesCache.Values)
                    {
                        cache.RemoveAll(m => m.Id == messageId);
                    }
                }
                return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
    #endregion

    #region Additional Methods
    public async Task<IReadOnlyList<Message>> GetUserMessagesAsync(string userId, int limit = 100)
    {
        try
        {
            lock (_cacheLock)
            {
                if (_userMessagesCache.TryGetValue(userId, out var messages))
                {
                    return messages.TakeLast(limit).ToList();
                }
            }

            return new List<Message>();
        }
        catch (Exception)
        {
            return new List<Message>();
        }
    }

    public async Task<int> GetMessageCountAsync()
    {
        return _messages.Count;
    }

    public async Task<IReadOnlyList<Message>> SearchMessagesAsync(string searchTerm, string? userId = null, int limit = 100)
    {
        try
        {
            var query = _messages.Values.AsQueryable();

            if (!string.IsNullOrEmpty(userId))
            {
                query = query.Where(m => m.Sender == userId || m.Receiver == userId);
            }

            query = query.Where(m => m.Content.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

            return query.OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToList();
        }
        catch (Exception)
        {
            return new List<Message>();
        }
    }

    public async Task<bool> ClearAllMessagesAsync()
    {
        try
        {
            _messages.Clear();
            lock (_cacheLock)
            {
                _conversationCache.Clear();
                _userMessagesCache.Clear();
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    #endregion

    #region Helper Methods
    private string GetConversationKey(string clientId1, string clientId2)
    {
        // Create consistent key regardless of order
        var participants = new[] { clientId1, clientId2 }.OrderBy(x => x);
        return string.Join(":", participants);
    }

    private void CleanupExpiredMessages(object? state)
    {
        if (_disposed) return;

        try
        {
            var cutoffTime = DateTime.UtcNow - _messageRetention;
            var expiredMessages = _messages.Values
                .Where(m => m.Timestamp < cutoffTime)
                .Select(m => m.Id)
                .ToList();

            foreach (var messageId in expiredMessages)
            {
                _ = DeleteMessageAsync(messageId);
            }
        }
        catch (Exception)
        {
            // Log error if logging is available
        }
    }
    #endregion

    #region Disposal
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cleanupTimer?.Dispose();
            _messages.Clear();

            lock (_cacheLock)
            {
                _conversationCache.Clear();
                _userMessagesCache.Clear();
            }
        }
    }
    #endregion
}