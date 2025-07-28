using System.Collections.Concurrent;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Helpers;

/// <summary>
/// File-based message store for persistent storage
/// </summary>
public class FileMessageStore : IMessageStore, IDisposable
{
    #region Fields
    private readonly string _dataDirectory;
    private readonly string _messagesFile;
    private readonly string _indexFile;
    private readonly object _fileLock = new();
    private readonly Timer _flushTimer;
    private readonly ConcurrentDictionary<string, Message> _pendingWrites = new();
    private bool _disposed;
    #endregion

    #region Constructor
    public FileMessageStore(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MessagingSystem");
        _messagesFile = Path.Combine(_dataDirectory, "messages.json");
        _indexFile = Path.Combine(_dataDirectory, "index.json");

        Directory.CreateDirectory(_dataDirectory);

        _flushTimer = new Timer(FlushPendingWrites, null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }
    #endregion

    #region IMessageStore Implementation
    public async Task<bool> StoreMessageAsync(Message message)
    {
        try
        {
            _pendingWrites[message.Id] = message;
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
            var allMessages = await LoadAllMessagesAsync();

            var messages = allMessages.Values
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
        return await GetMessagesAsync(clientId1, clientId2, limit);
    }

    public async Task<Message?> GetMessageAsync(string messageId)
    {
        try
        {
            if (_pendingWrites.TryGetValue(messageId, out var pendingMessage))
                return pendingMessage;

            var allMessages = await LoadAllMessagesAsync();
            allMessages.TryGetValue(messageId, out var message);
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
            var message = await GetMessageAsync(messageId);
            if (message != null)
            {
                message.Metadata["DeliveredAt"] = DateTime.UtcNow;
                message.Metadata["Status"] = "Delivered";
                return await StoreMessageAsync(message);
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
            var message = await GetMessageAsync(messageId);
            if (message != null)
            {
                message.Metadata["ReadAt"] = DateTime.UtcNow;
                message.Metadata["Status"] = "Read";
                return await StoreMessageAsync(message);
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
            lock (_fileLock)
            {
                _pendingWrites.TryRemove(messageId, out _);

                var allMessages = LoadAllMessagesSync();
                if (allMessages.Remove(messageId))
                {
                    SaveAllMessagesSync(allMessages);
                    return true;
                }
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
    #endregion

    #region File Operations
    private async Task<Dictionary<string, Message>> LoadAllMessagesAsync()
    {
        return await Task.Run(LoadAllMessagesSync);
    }

    private Dictionary<string, Message> LoadAllMessagesSync()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(_messagesFile))
                    return new Dictionary<string, Message>();

                var json = File.ReadAllText(_messagesFile);
                var messages = JsonSerializer.Deserialize<Dictionary<string, Message>>(json) ?? new Dictionary<string, Message>();

                // Add pending writes
                foreach (var kvp in _pendingWrites)
                {
                    messages[kvp.Key] = kvp.Value;
                }

                return messages;
            }
            catch (Exception)
            {
                return new Dictionary<string, Message>();
            }
        }
    }

    private void SaveAllMessagesSync(Dictionary<string, Message> messages)
    {
        lock (_fileLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(messages, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_messagesFile, json);

                // Update index for faster lookups
                var index = messages.Values
                    .GroupBy(m => m.Sender)
                    .ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList());

                var indexJson = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_indexFile, indexJson);
            }
            catch (Exception)
            {
                // Log error if logging is available
            }
        }
    }

    private void FlushPendingWrites(object? state)
    {
        if (_disposed || _pendingWrites.IsEmpty) return;

        try
        {
            var allMessages = LoadAllMessagesSync();

            foreach (var kvp in _pendingWrites)
            {
                allMessages[kvp.Key] = kvp.Value;
            }

            SaveAllMessagesSync(allMessages);
            _pendingWrites.Clear();
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
            _flushTimer?.Dispose();
            FlushPendingWrites(null);
        }
    }
    #endregion
}