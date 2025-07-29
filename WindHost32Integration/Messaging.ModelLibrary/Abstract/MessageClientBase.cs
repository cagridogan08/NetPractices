using System.Collections.Concurrent;
using System.Text.Json;

namespace Messaging.ModelLibrary.Abstract;

/// <summary>
/// Enhanced base class for message clients with built-in client-to-client features
/// </summary>
public abstract class MessageClientBase : IMessageClient
{
    #region Fields

    protected IClientDiscovery? _clientDiscovery;
    protected IMessageStore? _messageStore;
    protected IGroupManager? _groupManager;
    protected readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingAcknowledgments = new();
    protected readonly ConcurrentDictionary<string, ClientInfo> _knownClients = new();
    protected readonly Timer? _heartbeatTimer;
    protected readonly Timer? _clientListRefreshTimer;
    protected volatile bool _disposed;

    #endregion

    #region Properties
    public abstract bool IsConnected { get; }
    public abstract ConnectionInfo? ConnectionInfo { get; protected set; }
    public abstract TransportType TransportType { get; }

    public IReadOnlyDictionary<string, ClientInfo> KnownClients => _knownClients;
    #endregion

    #region Events
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;
    public event EventHandler<ClientDiscoveryEventArgs>? ClientDiscovered;
    public event EventHandler<ClientDiscoveryEventArgs>? ClientDisconnected;
    #endregion

    #region Constructor
    protected MessageClientBase()
    {
        _heartbeatTimer = new Timer(SendHeartbeat, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _clientListRefreshTimer = new Timer(RefreshClientListPeriodically, null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
    }
    #endregion

    #region Abstract Methods
    public abstract Task<bool> ConnectAsync(Dictionary<string, object> configuration);
    public abstract Task DisconnectAsync();
    public abstract Task<bool> SendMessageAsync(Message message);
    #endregion

    #region Enhanced Messaging Methods
    public virtual async Task<bool> SendDirectMessageAsync(string recipientId, string content, MessageType type = MessageType.Text)
    {
        var message = new Message
        {
            Content = content,
            Receiver = recipientId,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Type = type,
            RequiresAcknowledgment = false
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> SendDirectMessageWithAckAsync(string recipientId, string content, TimeSpan timeout)
    {
        var message = new Message
        {
            Content = content,
            Receiver = recipientId,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Type = MessageType.Text,
            RequiresAcknowledgment = true
        };

        var tcs = new TaskCompletionSource<bool>();
        _pendingAcknowledgments[message.Id] = tcs;

        try
        {
            var sent = await SendMessageAsync(message);
            if (!sent)
            {
                _pendingAcknowledgments.TryRemove(message.Id, out _);
                return false;
            }

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() => tcs.TrySetResult(false));

            return await tcs.Task;
        }
        finally
        {
            _pendingAcknowledgments.TryRemove(message.Id, out _);
        }
    }

    public virtual async Task<bool> BroadcastMessageAsync(string content, MessageType type = MessageType.Text)
    {
        var message = new Message
        {
            Content = content,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = "*",
            Type = type
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> ReplyToMessageAsync(string originalMessageId, string content)
    {
        var message = new Message
        {
            Content = content,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Type = MessageType.Text,
            ReplyToId = originalMessageId
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> SendFileAsync(string recipientId, byte[] fileData, string fileName, string mimeType = "application/octet-stream")
    {
        var fileContent = Convert.ToBase64String(fileData);
        var message = new Message
        {
            Content = fileContent,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = recipientId,
            Type = MessageType.Text,
            Metadata = new Dictionary<string, object>
            {
                ["IsFile"] = true,
                ["FileName"] = fileName,
                ["MimeType"] = mimeType,
                ["FileSize"] = fileData.Length
            }
        };

        return await SendMessageAsync(message);
    }
    #endregion

    #region Enhanced Client Discovery
    public virtual async Task<IReadOnlyList<ClientInfo>> GetOnlineClientsAsync()
    {
        if (_clientDiscovery != null)
            return await _clientDiscovery.GetOnlineClientsAsync();

        // Request fresh client list from server
        await RefreshClientListAsync();

        // Return current known clients
        return _knownClients.Values.Where(c => c.IsOnline).ToList();
    }

    public virtual async Task<ClientInfo?> GetClientInfoAsync(string clientId)
    {
        if (_clientDiscovery != null)
            return await _clientDiscovery.GetClientAsync(clientId);

        _knownClients.TryGetValue(clientId, out var client);
        return client;
    }

    public virtual async Task<bool> IsClientOnlineAsync(string clientId)
    {
        if (_clientDiscovery != null)
            return await _clientDiscovery.IsClientOnlineAsync(clientId);

        return _knownClients.TryGetValue(clientId, out var client) && client.IsOnline;
    }

    public virtual async Task RefreshClientListAsync()
    {
        try
        {
            var message = new Message
            {
                Content = "REQUEST_CLIENT_LIST",
                Sender = ConnectionInfo?.Name ?? "Unknown",
                Receiver = "System",
                Type = MessageType.System
            };

            await SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to refresh client list: {ex.Message}", ex));
        }
    }

    private void RefreshClientListPeriodically(object? state)
    {
        if (IsConnected && !_disposed)
        {
            _ = Task.Run(RefreshClientListAsync);
        }
    }
    #endregion

    #region Group Management
    public virtual async Task<bool> CreateGroupAsync(string groupName)
    {
        if (_groupManager != null && ConnectionInfo != null)
            return await _groupManager.CreateGroupAsync(groupName, ConnectionInfo.Id);

        var message = new Message
        {
            Content = $"CREATE_GROUP:{groupName}",
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = "System",
            Type = MessageType.System
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> JoinGroupAsync(string groupName)
    {
        if (_groupManager != null && ConnectionInfo != null)
            return await _groupManager.JoinGroupAsync(groupName, ConnectionInfo.Id);

        var message = new Message
        {
            Content = $"JOIN_GROUP:{groupName}",
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = "System",
            Type = MessageType.System
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> LeaveGroupAsync(string groupName)
    {
        if (_groupManager != null && ConnectionInfo != null)
            return await _groupManager.LeaveGroupAsync(groupName, ConnectionInfo.Id);

        var message = new Message
        {
            Content = $"LEAVE_GROUP:{groupName}",
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = "System",
            Type = MessageType.System
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> SendGroupMessageAsync(string groupName, string content)
    {
        var message = new Message
        {
            Content = content,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = $"group:{groupName}",
            Type = MessageType.Text
        };

        return await SendMessageAsync(message);
    }
    #endregion

    #region Enhanced Event Handlers
    protected virtual void OnMessageReceived(MessageEventArgs e)
    {
        try
        {
            // Handle system messages
            if (e.Message.Type == MessageType.System)
            {
                HandleSystemMessage(e.Message);
                return;
            }

            // Handle acknowledgments
            if (e.Message.Type == MessageType.Acknowledgment && !string.IsNullOrEmpty(e.Message.ReplyToId))
            {
                if (_pendingAcknowledgments.TryRemove(e.Message.ReplyToId, out var tcs))
                {
                    tcs.SetResult(e.Message.Content == "ACK");
                }
                return;
            }

            // Update known clients
            if (!string.IsNullOrEmpty(e.Message.Sender) && e.Message.Sender != "System")
            {
                UpdateKnownClient(e.Message.Sender, isOnline: true);
            }

            MessageReceived?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling received message: {ex.Message}", ex));
        }
    }

    protected virtual void OnConnected(ConnectionEventArgs e)
    {
        Connected?.Invoke(this, e);

        // Request initial client list when connected
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000); // Give server time to register us
            await RefreshClientListAsync();
        });
    }

    protected virtual void OnDisconnected(ConnectionEventArgs e)
    {
        Disconnected?.Invoke(this, e);

        // Mark all known clients as offline
        foreach (var client in _knownClients.Values)
        {
            client.IsOnline = false;
        }
    }

    protected virtual void OnErrorOccurred(ErrorEventArgs e) => ErrorOccurred?.Invoke(this, e);
    protected virtual void OnClientDiscovered(ClientDiscoveryEventArgs e) => ClientDiscovered?.Invoke(this, e);
    protected virtual void OnClientDisconnected(ClientDiscoveryEventArgs e) => ClientDisconnected?.Invoke(this, e);
    #endregion

    #region Enhanced System Message Handling
    private void HandleSystemMessage(Message message)
    {
        try
        {
            if (message.Content.StartsWith("CLIENT_LIST:"))
            {
                HandleClientListResponse(message.Content);
            }
            else if (message.Content.StartsWith("CLIENT_JOINED:"))
            {
                HandleClientJoined(message.Content);
            }
            else if (message.Content.StartsWith("CLIENT_LEFT:"))
            {
                HandleClientLeft(message.Content);
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling system message: {ex.Message}", ex));
        }
    }

    private void HandleClientListResponse(string content)
    {
        try
        {
            var clientsJson = content.Substring("CLIENT_LIST:".Length);
            if (string.IsNullOrWhiteSpace(clientsJson)) return;

            var clientList = JsonSerializer.Deserialize<List<JsonElement>>(clientsJson);
            if (clientList == null) return;

            // Mark all current clients as offline first
            foreach (var client in _knownClients.Values)
            {
                client.IsOnline = false;
            }

            // Update with fresh list
            foreach (var clientElement in clientList)
            {
                try
                {
                    var id = clientElement.GetProperty("Id").GetString();
                    var name = clientElement.GetProperty("Name").GetString();
                    var address = clientElement.GetProperty("Address").GetString();
                    var connectedAtStr = clientElement.GetProperty("ConnectedAt").GetString();
                    var transportTypeStr = clientElement.GetProperty("TransportType").GetString();

                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name)) continue;

                    var clientInfo = new ClientInfo
                    {
                        Id = id,
                        Name = name,
                        DisplayName = name,
                        IsOnline = true,
                        LastSeen = DateTime.UtcNow,
                        Address = address ?? "Unknown",
                        TransportType = Enum.TryParse<TransportType>(transportTypeStr, out var transport) ? transport : TransportType
                    };

                    if (DateTime.TryParse(connectedAtStr, out var connectedAt))
                    {
                        clientInfo.Properties["ConnectedAt"] = connectedAt;
                    }

                    var isNewClient = !_knownClients.ContainsKey(id);
                    _knownClients[id] = clientInfo;

                    if (isNewClient)
                    {
                        OnClientDiscovered(new ClientDiscoveryEventArgs(clientInfo, true));
                    }
                }
                catch (Exception ex)
                {
                    OnErrorOccurred(new ErrorEventArgs($"Error parsing client info: {ex.Message}", ex));
                }
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error parsing client list response: {ex.Message}", ex));
        }
    }

    private void HandleClientJoined(string content)
    {
        try
        {
            var clientName = content.Substring("CLIENT_JOINED:".Length);
            if (string.IsNullOrWhiteSpace(clientName)) return;

            var clientInfo = new ClientInfo
            {
                Id = clientName,
                Name = clientName,
                DisplayName = clientName,
                IsOnline = true,
                LastSeen = DateTime.UtcNow,
                TransportType = TransportType
            };

            _knownClients[clientName] = clientInfo;
            OnClientDiscovered(new ClientDiscoveryEventArgs(clientInfo, true));
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling client joined: {ex.Message}", ex));
        }
    }

    private void HandleClientLeft(string content)
    {
        try
        {
            var clientName = content.Substring("CLIENT_LEFT:".Length);
            if (string.IsNullOrWhiteSpace(clientName)) return;

            if (_knownClients.TryGetValue(clientName, out var client))
            {
                client.IsOnline = false;
                OnClientDisconnected(new ClientDiscoveryEventArgs(client, false));
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling client left: {ex.Message}", ex));
        }
    }

    private void UpdateKnownClient(string clientName, bool isOnline)
    {
        if (clientName == ConnectionInfo?.Name) return; // Don't track ourselves

        _knownClients.AddOrUpdate(clientName,
            new ClientInfo
            {
                Id = clientName,
                Name = clientName,
                DisplayName = clientName,
                IsOnline = isOnline,
                LastSeen = DateTime.UtcNow,
                TransportType = TransportType
            },
            (_, existing) =>
            {
                existing.LastSeen = DateTime.UtcNow;
                existing.IsOnline = isOnline;
                return existing;
            });
    }
    #endregion

    #region Helper Methods
    private void SendHeartbeat(object? state)
    {
        if (!IsConnected || _disposed) return;

        try
        {
            var heartbeatMessage = new Message
            {
                Content = "HEARTBEAT",
                Sender = ConnectionInfo?.Name ?? "Unknown",
                Receiver = "System",
                Type = MessageType.System
            };

            _ = Task.Run(async () => await SendMessageAsync(heartbeatMessage));
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Heartbeat error: {ex.Message}", ex));
        }
    }
    #endregion

    #region Disposal
    public virtual void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _heartbeatTimer?.Dispose();
            _clientListRefreshTimer?.Dispose();

            // Complete all pending acknowledgments
            foreach (var tcs in _pendingAcknowledgments.Values)
            {
                tcs.TrySetResult(false);
            }
            _pendingAcknowledgments.Clear();
            _knownClients.Clear();
        }
    }
    #endregion
}