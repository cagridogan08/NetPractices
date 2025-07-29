using System.Collections.Concurrent;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Broker;

/// <summary>
/// Central message broker that coordinates client-to-client communication across transports
/// </summary>
public class MessageBroker : IMessageRouter, IClientDiscovery, IGroupManager, IDisposable
{
    #region Fields
    private readonly ConcurrentDictionary<string, IMessageTransport> _transports = new();
    private readonly ConcurrentDictionary<string, ClientInfo> _clients = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _clientGroups = new();
    private readonly ConcurrentDictionary<string, string> _clientToTransport = new();
    private readonly ConcurrentDictionary<string, DateTime> _clientHeartbeats = new();
    private readonly IMessageStore? _messageStore;
    private readonly Timer _heartbeatTimer;
    private readonly object _lockObject = new();
    private bool _disposed;
    #endregion

    #region Events

    public event EventHandler<ClientDiscoveryEventArgs>? ClientDiscovered;
    public event EventHandler<ClientDiscoveryEventArgs>? ClientDisconnected;
    public event EventHandler<MessageEventArgs>? MessageRouted;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Constructor
    public MessageBroker(IMessageStore? messageStore = null)
    {
        _messageStore = messageStore;
        _heartbeatTimer = new Timer(CleanupStaleClients, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }
    #endregion

    #region Transport Management
    public void RegisterTransport(IMessageTransport transport)
    {
        lock (_lockObject)
        {
            var transportId = $"{transport.TransportType}_{Guid.NewGuid()}";
            _transports[transportId] = transport;

            // Subscribe to transport events
            transport.ClientConnected += OnTransportClientConnected;
            transport.ClientDisconnected += OnTransportClientDisconnected;
            transport.MessageReceived += OnTransportMessageReceived;
            transport.ErrorOccurred += OnTransportErrorOccurred;
        }
    }

    public void UnregisterTransport(IMessageTransport transport)
    {
        lock (_lockObject)
        {
            var transportToRemove = _transports.FirstOrDefault(kvp => kvp.Value == transport);
            if (transportToRemove.Key != null)
            {
                _transports.TryRemove(transportToRemove.Key, out _);

                // Unsubscribe from transport events
                transport.ClientConnected -= OnTransportClientConnected;
                transport.ClientDisconnected -= OnTransportClientDisconnected;
                transport.MessageReceived -= OnTransportMessageReceived;
                transport.ErrorOccurred -= OnTransportErrorOccurred;

                // Remove clients from this transport
                var clientsToRemove = _clientToTransport
                    .Where(kvp => kvp.Value == transportToRemove.Key)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var clientId in clientsToRemove)
                {
                    RemoveClientFromBroker(clientId);
                }
            }
        }
    }
    #endregion

    #region Enhanced Message Routing
    public async Task<bool> RouteMessageAsync(Message message, IMessageTransport sourceTransport)
    {
        try
        {
            // Validate message
            if (!ValidateMessage(message))
            {
                await SendErrorMessageAsync(message.Sender, "Invalid message format", sourceTransport);
                return false;
            }

            // Store message if store is available
            if (_messageStore != null)
            {
                await _messageStore.StoreMessageAsync(message);
            }

            // Update sender heartbeat
            UpdateClientHeartbeat(message.Sender);

            // Route based on message type and receiver
            var success = message.Receiver switch
            {
                "*" => await BroadcastMessageAsync(message, sourceTransport),
                var receiver when receiver.StartsWith("group:") => await RouteGroupMessageAsync(receiver.Substring(6), message, sourceTransport),
                _ => await RouteDirectMessageAsync(message, sourceTransport)
            };

            if (success)
            {
                MessageRouted?.Invoke(this, new MessageEventArgs(message));
            }

            return success;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message routing failed: {ex.Message}", ex));
            return false;
        }
    }

    private async Task<bool> RouteDirectMessageAsync(Message message, IMessageTransport sourceTransport)
    {
        // Check if target client exists and is online
        if (!_clients.TryGetValue(message.Receiver, out var targetClient) || !targetClient.IsOnline)
        {
            await SendErrorMessageAsync(message.Sender, $"Client '{message.Receiver}' is not available", sourceTransport);
            return false;
        }

        // Find target transport
        if (!_clientToTransport.TryGetValue(message.Receiver, out var transportId) ||
            !_transports.TryGetValue(transportId, out var targetTransport))
        {
            await SendErrorMessageAsync(message.Sender, $"Client '{message.Receiver}' is not reachable", sourceTransport);
            return false;
        }

        // Send message to target
        var success = await targetTransport.SendMessageAsync(message, message.Receiver);

        // Send acknowledgment if requested
        if (message.RequiresAcknowledgment)
        {
            await SendAcknowledgmentAsync(message, success, sourceTransport);
        }

        // Mark as delivered in store
        if (success && _messageStore != null)
        {
            await _messageStore.MarkMessageAsDeliveredAsync(message.Id);
        }

        return success;
    }

    private async Task<bool> BroadcastMessageAsync(Message message, IMessageTransport sourceTransport)
    {
        var tasks = new List<Task<bool>>();
        var sourceTransportId = _transports.FirstOrDefault(kvp => kvp.Value == sourceTransport).Key;

        foreach (var kvp in _transports)
        {
            if (kvp.Key != sourceTransportId) // Don't send back to source transport
            {
                tasks.Add(kvp.Value.BroadcastMessageAsync(message));
            }
        }

        if (tasks.Count == 0) return false;

        var results = await Task.WhenAll(tasks);
        return results.Any(r => r);
    }

    private async Task<bool> RouteGroupMessageAsync(string groupName, Message message, IMessageTransport sourceTransport)
    {
        if (!_groups.TryGetValue(groupName, out var groupMembers) || groupMembers.Count == 0)
        {
            await SendErrorMessageAsync(message.Sender, $"Group '{groupName}' not found or empty", sourceTransport);
            return false;
        }

        var tasks = new List<Task<bool>>();
        var successCount = 0;

        foreach (var memberId in groupMembers.Where(id => id != message.Sender))
        {
            if (_clientToTransport.TryGetValue(memberId, out var transportId) &&
                _transports.TryGetValue(transportId, out var transport) &&
                _clients.TryGetValue(memberId, out var client) && client.IsOnline)
            {
                var groupMessage = CreateGroupMessage(message, groupName, memberId);
                tasks.Add(transport.SendMessageAsync(groupMessage, memberId));
            }
        }

        if (tasks.Count > 0)
        {
            var results = await Task.WhenAll(tasks);
            successCount = results.Count(r => r);
        }

        return successCount > 0;
    }

    public async Task<bool> CanRouteToClientAsync(string clientId, IMessageTransport transport)
    {
        return _clients.ContainsKey(clientId) &&
               _clientToTransport.ContainsKey(clientId) &&
               _clients[clientId].IsOnline;
    }
    #endregion

    #region Client Discovery
    public async Task<IReadOnlyList<ClientInfo>> GetOnlineClientsAsync()
    {
        return _clients.Values.Where(c => c.IsOnline).ToList();
    }

    public async Task<ClientInfo?> GetClientAsync(string clientId)
    {
        _clients.TryGetValue(clientId, out var client);
        return client;
    }

    public async Task<bool> IsClientOnlineAsync(string clientId)
    {
        return _clients.TryGetValue(clientId, out var client) && client.IsOnline;
    }

    public async Task RegisterClientAsync(ClientInfo client)
    {
        lock (_lockObject)
        {
            client.IsOnline = true;
            client.LastSeen = DateTime.UtcNow;
            _clients[client.Id] = client;
            UpdateClientHeartbeat(client.Id);
        }

        ClientDiscovered?.Invoke(this, new ClientDiscoveryEventArgs(client, true));
    }

    public async Task UnregisterClientAsync(string clientId)
    {
        RemoveClientFromBroker(clientId);
    }

    public async Task UpdateClientPresenceAsync(string clientId, DateTime lastSeen)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            client.LastSeen = lastSeen;
            client.IsOnline = DateTime.UtcNow.Subtract(lastSeen).TotalMinutes < 5;
            UpdateClientHeartbeat(clientId);
        }
    }
    #endregion

    #region Group Management  
    public async Task<bool> CreateGroupAsync(string groupName, string creatorId)
    {
        lock (_lockObject)
        {
            if (_groups.ContainsKey(groupName))
                return false;

            _groups[groupName] = new HashSet<string> { creatorId };

            if (!_clientGroups.TryGetValue(creatorId, out var clientGroups))
            {
                clientGroups = new HashSet<string>();
                _clientGroups[creatorId] = clientGroups;
            }
            clientGroups.Add(groupName);
        }

        return true;
    }

    public async Task<bool> DeleteGroupAsync(string groupName)
    {
        lock (_lockObject)
        {
            if (!_groups.TryRemove(groupName, out var members))
                return false;

            // Remove group from all member's group lists
            foreach (var memberId in members)
            {
                if (_clientGroups.TryGetValue(memberId, out var memberGroups))
                {
                    memberGroups.Remove(groupName);
                }
            }
        }

        return true;
    }

    public async Task<bool> JoinGroupAsync(string groupName, string clientId)
    {
        lock (_lockObject)
        {
            if (!_groups.TryGetValue(groupName, out var groupMembers))
            {
                groupMembers = new HashSet<string>();
                _groups[groupName] = groupMembers;
            }

            groupMembers.Add(clientId);

            if (!_clientGroups.TryGetValue(clientId, out var clientGroups))
            {
                clientGroups = new HashSet<string>();
                _clientGroups[clientId] = clientGroups;
            }
            clientGroups.Add(groupName);
        }

        return true;
    }

    public async Task<bool> LeaveGroupAsync(string groupName, string clientId)
    {
        lock (_lockObject)
        {
            var success = false;

            if (_groups.TryGetValue(groupName, out var groupMembers))
            {
                success = groupMembers.Remove(clientId);

                if (groupMembers.Count == 0)
                {
                    _groups.TryRemove(groupName, out _);
                }
            }

            if (_clientGroups.TryGetValue(clientId, out var clientGroups))
            {
                clientGroups.Remove(groupName);
            }

            return success;
        }
    }

    public async Task<IReadOnlyList<string>> GetGroupMembersAsync(string groupName)
    {
        if (_groups.TryGetValue(groupName, out var members))
        {
            return members.ToList();
        }
        return new List<string>();
    }

    public async Task<IReadOnlyList<string>> GetClientGroupsAsync(string clientId)
    {
        if (_clientGroups.TryGetValue(clientId, out var groups))
        {
            return groups.ToList();
        }
        return new List<string>();
    }

    public async Task<bool> SendGroupMessageAsync(string groupName, Message message)
    {
        message.Receiver = $"group:{groupName}";
        return await RouteMessageAsync(message, null!);
    }
    #endregion

    #region Event Handlers
    private async void OnTransportClientConnected(object? sender, ConnectionEventArgs e)
    {
        if (sender is IMessageTransport transport)
        {
            var transportId = _transports.FirstOrDefault(kvp => kvp.Value == transport).Key;
            if (transportId != null)
            {
                var client = new ClientInfo
                {
                    Id = e.Connection.Id,
                    Name = e.Connection.Name,
                    DisplayName = e.Connection.Name,
                    TransportType = transport.TransportType,
                    Address = e.Connection.Address,
                    LastSeen = DateTime.UtcNow,
                    IsOnline = true,
                    Properties = new Dictionary<string, object>(e.Connection.Properties)
                };

                _clientToTransport[e.Connection.Id] = transportId;
                await RegisterClientAsync(client);
            }
        }
    }

    private async void OnTransportClientDisconnected(object? sender, ConnectionEventArgs e)
    {
        await UnregisterClientAsync(e.Connection.Id);
    }

    private async void OnTransportMessageReceived(object? sender, MessageEventArgs e)
    {
        if (sender is IMessageTransport transport)
        {
            await RouteMessageAsync(e.Message, transport);
        }
    }

    private void OnTransportErrorOccurred(object? sender, ErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, e);
    }
    #endregion

    #region Helper Methods
    private bool ValidateMessage(Message message)
    {
        return !string.IsNullOrEmpty(message.Id) &&
               !string.IsNullOrEmpty(message.Sender) &&
               !string.IsNullOrEmpty(message.Receiver) &&
               (!message.ExpiresIn.HasValue || message.Timestamp.Add(message.ExpiresIn.Value) >= DateTime.UtcNow);
    }

    private Message CreateGroupMessage(Message originalMessage, string groupName, string receiverId)
    {
        return new Message
        {
            Id = originalMessage.Id,
            Content = originalMessage.Content,
            Sender = originalMessage.Sender,
            Receiver = receiverId,
            Type = originalMessage.Type,
            Timestamp = originalMessage.Timestamp,
            Priority = originalMessage.Priority,
            ReplyToId = originalMessage.ReplyToId,
            RequiresAcknowledgment = originalMessage.RequiresAcknowledgment,
            ExpiresIn = originalMessage.ExpiresIn,
            Tags = originalMessage.Tags,
            Metadata = new Dictionary<string, object>(originalMessage.Metadata)
            {
                ["GroupName"] = groupName,
                ["IsGroupMessage"] = true,
                ["OriginalReceiver"] = originalMessage.Receiver
            }
        };
    }

    private async Task SendErrorMessageAsync(string recipientId, string error, IMessageTransport transport)
    {
        var errorMessage = new Message
        {
            Content = error,
            Sender = "System",
            Receiver = recipientId,
            Type = MessageType.Error,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await transport.SendMessageAsync(errorMessage, recipientId);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send error message: {ex.Message}", ex));
        }
    }

    private async Task SendAcknowledgmentAsync(Message originalMessage, bool success, IMessageTransport transport)
    {
        var ackMessage = new Message
        {
            Content = success ? "ACK" : "NACK",
            Sender = "System",
            Receiver = originalMessage.Sender,
            Type = MessageType.Acknowledgment,
            ReplyToId = originalMessage.Id,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            await transport.SendMessageAsync(ackMessage, originalMessage.Sender);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send acknowledgment: {ex.Message}", ex));
        }
    }

    private void UpdateClientHeartbeat(string clientId)
    {
        _clientHeartbeats[clientId] = DateTime.UtcNow;
    }

    private void CleanupStaleClients(object? state)
    {
        try
        {
            var staleThreshold = DateTime.UtcNow.AddMinutes(-5);
            var staleClients = _clientHeartbeats
                .Where(kvp => kvp.Value < staleThreshold)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var clientId in staleClients)
            {
                RemoveClientFromBroker(clientId);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Cleanup error: {ex.Message}", ex));
        }
    }

    private void RemoveClientFromBroker(string clientId)
    {
        lock (_lockObject)
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                _clientToTransport.TryRemove(clientId, out _);
                _clientHeartbeats.TryRemove(clientId, out _);

                // Remove from all groups
                if (_clientGroups.TryGetValue(clientId, out var groups))
                {
                    foreach (var groupName in groups.ToList())
                    {
                        if (_groups.TryGetValue(groupName, out var groupMembers))
                        {
                            groupMembers.Remove(clientId);
                            if (groupMembers.Count == 0)
                            {
                                _groups.TryRemove(groupName, out _);
                            }
                        }
                    }
                    _clientGroups.TryRemove(clientId, out _);
                }

                client.IsOnline = false;
                ClientDisconnected?.Invoke(this, new ClientDiscoveryEventArgs(client, false));
            }
        }
    }
    #endregion

    #region Disposal
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _heartbeatTimer?.Dispose();

            // Unregister all transports
            foreach (var transport in _transports.Values.ToList())
            {
                UnregisterTransport(transport);
            }

            _transports.Clear();
            _clients.Clear();
            _groups.Clear();
            _clientGroups.Clear();
            _clientToTransport.Clear();
            _clientHeartbeats.Clear();
        }
    }
    #endregion
}
/// <summary>
/// Factory for creating and configuring message brokers
/// </summary>
public class MessageBrokerFactory
{
    public static MessageBroker CreateBroker(IMessageStore? messageStore = null)
    {
        return new MessageBroker(messageStore);
    }

    public static MessageBroker CreateBrokerWithTransports(
        IEnumerable<IMessageTransport> transports,
        IMessageStore? messageStore = null)
    {
        var broker = new MessageBroker(messageStore);

        foreach (var transport in transports)
        {
            broker.RegisterTransport(transport);
        }

        return broker;
    }
}