using System.Collections.Concurrent;


namespace Messaging.ModelLibrary.Abstract
{
    public abstract class MessageTransportBase : IMessageTransport
    {
        #region Fields

        protected readonly ConcurrentDictionary<string, ClientConnectionInfo> _connections = new();
        protected readonly Timer _clientTimeoutTimer;
        protected volatile bool _disposed;
        protected readonly object _lockObject = new();

        #endregion

        #region Events
        public event EventHandler<MessageEventArgs>? MessageReceived;
        public event EventHandler<ConnectionEventArgs>? ClientConnected;
        public event EventHandler<ConnectionEventArgs>? ClientDisconnected;
        public event EventHandler<ErrorEventArgs>? ErrorOccurred;
        #endregion

        #region Properties
        public abstract bool IsRunning { get; protected set; }
        public abstract TransportType TransportType { get; }
        public virtual IReadOnlyList<ConnectionInfo> Connections =>
            _connections.Values.Select(c => c.ConnectionInfo).ToList();
        #endregion

        #region Constructor
        protected MessageTransportBase()
        {
            _clientTimeoutTimer = new Timer(CheckForTimeoutClients, null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
        #endregion

        #region Abstract Methods
        public abstract Task<bool> StartAsync(Dictionary<string, object>? configuration = null);
        public abstract Task StopAsync();
        public abstract Task<bool> SendMessageAsync(Message message, string? connectionId = null);
        public abstract Task<bool> BroadcastMessageAsync(Message message);
        #endregion

        #region Client Management
        protected virtual void RegisterClient(string clientId, string clientName, object transportSpecificData = null)
        {
            lock (_lockObject)
            {
                var connectionInfo = new ConnectionInfo
                {
                    Id = clientId,
                    Name = clientName,
                    Address = GetClientAddress(transportSpecificData),
                    ConnectedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var clientConnectionInfo = new ClientConnectionInfo
                {
                    ConnectionInfo = connectionInfo,
                    LastActivity = DateTime.UtcNow,
                    TransportData = transportSpecificData
                };

                _connections.AddOrUpdate(clientId, clientConnectionInfo, (key, existing) =>
                {
                    existing.LastActivity = DateTime.UtcNow;
                    existing.ConnectionInfo.IsActive = true;
                    return existing;
                });

                ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
            }
        }

        protected virtual void UnregisterClient(string clientId)
        {
            lock (_lockObject)
            {
                if (_connections.TryRemove(clientId, out var clientInfo))
                {
                    clientInfo.ConnectionInfo.IsActive = false;
                    ClientDisconnected?.Invoke(this, new ConnectionEventArgs(clientInfo.ConnectionInfo));
                }
            }
        }

        protected virtual void UpdateClientActivity(string clientId)
        {
            if (_connections.TryGetValue(clientId, out var clientInfo))
            {
                clientInfo.LastActivity = DateTime.UtcNow;
            }
        }

        protected virtual string GetClientAddress(object transportSpecificData)
        {
            return transportSpecificData?.ToString() ?? "Unknown";
        }

        private void CheckForTimeoutClients(object? state)
        {
            if (_disposed) return;

            try
            {
                var timeoutThreshold = DateTime.UtcNow.AddMinutes(-5);
                var timedOutClients = _connections.Values
                    .Where(c => c.LastActivity < timeoutThreshold)
                    .Select(c => c.ConnectionInfo.Id)
                    .ToList();

                foreach (var clientId in timedOutClients)
                {
                    UnregisterClient(clientId);
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"Client timeout check error: {ex.Message}", ex));
            }
        }
        #endregion

        #region Message Handling
        protected virtual void HandleReceivedMessage(Message message, string clientId)
        {
            try
            {
                UpdateClientActivity(clientId);

                // Handle system messages
                if (message.Type == MessageType.System)
                {
                    HandleSystemMessage(message, clientId);
                    return;
                }

                // Get connection info for the message event
                if (_connections.TryGetValue(clientId, out var clientInfo))
                {
                    MessageReceived?.Invoke(this, new MessageEventArgs(message, clientInfo.ConnectionInfo));
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"Error handling message: {ex.Message}", ex));
            }
        }

        protected virtual void HandleSystemMessage(Message message, string clientId)
        {
            try
            {
                switch (message.Content)
                {
                    case "CLIENT_REGISTER":
                        RegisterClient(clientId, message.Sender);
                        break;
                    case "CLIENT_UNREGISTER":
                        UnregisterClient(clientId);
                        break;
                    case "HEARTBEAT":
                        UpdateClientActivity(clientId);
                        break;
                    case var content when content.StartsWith("REQUEST_CLIENT_LIST"):
                        _ = Task.Run(() => SendClientListAsync(clientId));
                        break;
                }
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"System message handling error: {ex.Message}", ex));
            }
        }

        protected virtual async Task SendClientListAsync(string requestingClientId)
        {
            try
            {
                var onlineClients = _connections.Values
                    .Where(c => c.ConnectionInfo.IsActive && c.ConnectionInfo.Id != requestingClientId)
                    .Select(c => new { c.ConnectionInfo.Id, c.ConnectionInfo.Name })
                    .ToList();

                var clientListJson = System.Text.Json.JsonSerializer.Serialize(onlineClients);

                var responseMessage = new Message
                {
                    Content = $"CLIENT_LIST:{clientListJson}",
                    Sender = "System",
                    Receiver = requestingClientId,
                    Type = MessageType.System
                };

                await SendMessageAsync(responseMessage, requestingClientId);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"Send client list error: {ex.Message}", ex));
            }
        }
        #endregion

        #region Event Helpers
        protected virtual void OnErrorOccurred(ErrorEventArgs e) => ErrorOccurred?.Invoke(this, e);

        protected virtual void OnClientDisconnected(ConnectionEventArgs e) => ClientDisconnected?.Invoke(this, e);
        #endregion

        #region Disposal
        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _clientTimeoutTimer?.Dispose();

                try
                {
                    StopAsync().Wait(3000);
                }
                catch { }
            }
        }
        #endregion

        #region Helper Classes
        protected class ClientConnectionInfo
        {
            public ConnectionInfo ConnectionInfo { get; set; }
            public DateTime LastActivity { get; set; }
            public object TransportData { get; set; }
        }
        #endregion
    }
}
