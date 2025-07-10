using System.Collections.Concurrent;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Broker;

/// <summary>
/// Central message broker that coordinates client-to-client communication across transports
/// </summary>
public class MessageBroker : IMessageRouter, IClientDiscovery, IGroupManager
{
    #region Fields

    private readonly ConcurrentDictionary<string, IMessageTransport> _transports = new();
    private readonly ConcurrentDictionary<string, ClientInfo> _clients = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _clientGroups = new();
    private readonly ConcurrentDictionary<string, string> _clientToTransport = new();
    private readonly IMessageStore? _messageStore;
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
    }
    #endregion

    #region Transport Management
    public void RegisterTransport(IMessageTransport transport)
    {
        var transportId = $"{transport.TransportType}_{Guid.NewGuid()}";
        _transports[transportId] = transport;

        // Subscribe to transport events
        transport.ClientConnected += OnTransportClientConnected;
        transport.ClientDisconnected += OnTransportClientDisconnected;
        transport.MessageReceived += OnTransportMessageReceived;
        transport.ErrorOccurred += OnTransportErrorOccurred;
    }

    public void UnregisterTransport(IMessageTransport transport)
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
                _clientToTransport.TryRemove(clientId, out _);
                if (_clients.TryRemove(clientId, out var client))
                {
                    ClientDisconnected?.Invoke(this, new ClientDiscoveryEventArgs(client, false));
                }
            }
        }
    }
    #endregion

    #region Message Routing
    public async Task<bool> RouteMessageAsync(Message message, IMessageTransport sourceTransport)
    {
        try
        {
            // Store message if store is available
            if (_messageStore != null)
            {
                await _messageStore.StoreMessageAsync(message);
            }

            // Handle different message types
            switch (message.Receiver)
            {
                case "*": // Broadcast
                    return await BroadcastMessageAsync(message, sourceTransport);

                case var receiver when receiver.StartsWith("group:"):
                    var groupName = receiver.Substring(6);
                    return await RouteGroupMessageAsync(groupName, message, sourceTransport);

                default: // Direct message
                    return await RouteDirectMessageAsync(message, sourceTransport);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message routing failed: {ex.Message}", ex));
            return false;
        }
    }

    private async Task<bool> RouteDirectMessageAsync(Message message, IMessageTransport sourceTransport)
    {
        // Find the target client
        if (!_clients.TryGetValue(message.Receiver, out _))
        {
            // Client not found, send error back to sender
            await SendErrorMessageAsync(message.Sender, $"Client '{message.Receiver}' not found", sourceTransport);
            return false;
        }

        // Find the transport for the target client
        if (!_clientToTransport.TryGetValue(message.Receiver, out var transportId) ||
            !_transports.TryGetValue(transportId, out var targetTransport))
        {
            await SendErrorMessageAsync(message.Sender, $"Client '{message.Receiver}' is not reachable", sourceTransport);
            return false;
        }

        // Route message to target transport
        var success = await targetTransport.SendMessageAsync(message, message.Receiver);

        // Send acknowledgment if requested
        if (message.RequiresAcknowledgment)
        {
            await SendAcknowledgmentAsync(message, success, sourceTransport);
        }

        if (success)
        {
            MessageRouted?.Invoke(this, new MessageEventArgs(message));
        }

        return success;
    }

    private async Task<bool> BroadcastMessageAsync(Message message, IMessageTransport sourceTransport)
    {
        var tasks = new List<Task<bool>>();

        foreach (var transport in _transports.Values)
        {
            if (transport != sourceTransport) // Don't send back to source
            {
                tasks.Add(transport.BroadcastMessageAsync(message));
            }
        }

        var results = await Task.WhenAll(tasks);
        return results.Any(r => r);
    }

    private async Task<bool> RouteGroupMessageAsync(string groupName, Message message, IMessageTransport sourceTransport)
    {
        if (!_groups.TryGetValue(groupName, out var groupMembers))
        {
            await SendErrorMessageAsync(message.Sender, $"Group '{groupName}' not found", sourceTransport);
            return false;
        }

        var tasks = new List<Task<bool>>();

        foreach (var memberId in groupMembers)
        {
            if (memberId != message.Sender && // Don't send to sender
                _clientToTransport.TryGetValue(memberId, out var transportId) &&
                _transports.TryGetValue(transportId, out var transport))
            {
                var groupMessage = new Message
                {
                    Id = message.Id,
                    Content = message.Content,
                    Sender = message.Sender,
                    Receiver = memberId,
                    Type = message.Type,
                    Timestamp = message.Timestamp,
                    Metadata = new Dictionary<string, object>(message.Metadata)
                    {
                        ["GroupName"] = groupName,
                        ["IsGroupMessage"] = true
                    }
                };

                tasks.Add(transport.SendMessageAsync(groupMessage, memberId));
            }
        }

        var results = await Task.WhenAll(tasks);
        return results.Any(r => r);
    }

    public async Task<bool> CanRouteToClientAsync(string clientId, IMessageTransport transport)
    {
        return _clients.ContainsKey(clientId) && _clientToTransport.ContainsKey(clientId);
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
        _clients[client.Id] = client;
        ClientDiscovered?.Invoke(this, new ClientDiscoveryEventArgs(client, true));
    }

    public async Task UnregisterClientAsync(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            _clientToTransport.TryRemove(clientId, out _);

            // Remove from all groups
            var groupsToUpdate = _clientGroups.GetValueOrDefault(clientId, new HashSet<string>());
            foreach (var groupName in groupsToUpdate)
            {
                if (_groups.TryGetValue(groupName, out var groupMembers))
                {
                    groupMembers.Remove(clientId);
                }
            }
            _clientGroups.TryRemove(clientId, out _);

            ClientDisconnected?.Invoke(this, new ClientDiscoveryEventArgs(client, false));
        }
    }

    public async Task UpdateClientPresenceAsync(string clientId, DateTime lastSeen)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            client.LastSeen = lastSeen;
            client.IsOnline = DateTime.UtcNow.Subtract(lastSeen).TotalMinutes < 5; // Consider offline after 5 minutes
        }
    }
    #endregion

    #region Group Management
    public async Task<bool> CreateGroupAsync(string groupName, string creatorId)
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

        return true;
    }

    public async Task<bool> DeleteGroupAsync(string groupName)
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

        return true;
    }

    public async Task<bool> JoinGroupAsync(string groupName, string clientId)
    {
        if (!_groups.TryGetValue(groupName, out var groupMembers))
        {
            // Auto-create group if it doesn't exist
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

        return true;
    }

    public async Task<bool> LeaveGroupAsync(string groupName, string clientId)
    {
        var success = false;

        if (_groups.TryGetValue(groupName, out var groupMembers))
        {
            success = groupMembers.Remove(clientId);

            // Remove empty groups
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
        // This is handled by RouteGroupMessageAsync
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
    private async Task SendErrorMessageAsync(string recipientId, string error, IMessageTransport transport)
    {
        var errorMessage = new Message
        {
            Content = error,
            Sender = "System",
            Receiver = recipientId,
            Type = MessageType.Error
        };

        await transport.SendMessageAsync(errorMessage, recipientId);
    }

    private async Task SendAcknowledgmentAsync(Message originalMessage, bool success, IMessageTransport transport)
    {
        var ackMessage = new Message
        {
            Content = success ? "ACK" : "NACK",
            Sender = "System",
            Receiver = originalMessage.Sender,
            Type = MessageType.Acknowledgment,
            ReplyToId = originalMessage.Id
        };

        await transport.SendMessageAsync(ackMessage, originalMessage.Sender);
    }
    #endregion

    #region Disposal
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

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