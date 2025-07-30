using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Configuration;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Messaging.ModelLibrary.ZeroMQ;

public class ZeroMqTransport : MessageTransportBase
{
    #region Fields
    private PublisherSocket? _publisher;
    private RouterSocket? _router;
    private NetMQPoller? _poller;
    private readonly ConcurrentDictionary<string, ZeroMqClientInfo> _clients = new();
    private ZeroMqConfig? _config;
    private volatile bool _running;
    private Task? _pollerTask;
    #endregion

    #region Properties
    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.ZeroMQ;
    #endregion

    #region Methods
    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            _config = ZeroMqConfig.FromDictionary(configuration);

            // Create and configure publisher socket
            _publisher = new PublisherSocket();
            _publisher.Bind($"tcp://*:{_config.PublisherPort}");

            // Create and configure router socket
            _router = new RouterSocket();
            _router.Bind($"tcp://*:{_config.RouterPort}");
            // Set socket options
            _router.Options.ReconnectInterval = TimeSpan.FromMilliseconds(_config.SendTimeout);
            _router.Options.HeartbeatInterval = TimeSpan.FromMilliseconds(_config.ReceiveTimeout);

            // Setup event handler
            _router.ReceiveReady += OnRouterReceiveReady;

            // Create and start poller
            _poller = new NetMQPoller { _router };
            _running = true;

            // Start poller in background task
            _pollerTask = Task.Run(() =>
            {
                try
                {
                    _poller.Run();
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        OnErrorOccurred(new ErrorEventArgs($"ZeroMQ poller error: {ex.Message}", ex));
                    }
                }
            });

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to start ZeroMQ transport: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task StopAsync()
    {
        try
        {
            _running = false;
            IsRunning = false;

            // Stop the poller
            _poller?.Stop();

            // Wait for poller task to complete
            if (_pollerTask != null)
            {
                try
                {
                    await _pollerTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    // Force stop if timeout
                }
                _pollerTask = null;
            }

            // Dispose poller
            _poller?.Dispose();
            _poller = null;

            // Close and dispose sockets
            try
            {
                _publisher?.Close();
                _publisher?.Dispose();
                _publisher = null;

                _router?.Close();
                _router?.Dispose();
                _router = null;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"Error closing ZeroMQ sockets: {ex.Message}", ex));
            }

            // Clear client connections
            foreach (var client in _clients.Values)
            {
                UnregisterClient(client.ClientName);
            }
            _clients.Clear();
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error stopping ZeroMQ transport: {ex.Message}", ex));
        }
    }

    private void OnRouterReceiveReady(object? sender, NetMQSocketEventArgs e)
    {
        try
        {
            if (!_running || _router == null) return;

            // Receive identity frame
            var identity = _router.ReceiveFrameBytes();
            var identityString = Encoding.UTF8.GetString(identity);

            // Receive message frame
            var messageBytes = _router.ReceiveFrameBytes();
            var json = Encoding.UTF8.GetString(messageBytes);
            var message = JsonSerializer.Deserialize<Message>(json);

            if (message != null)
            {
                // Handle client registration
                if (message is { Type: MessageType.System, Content: "CLIENT_REGISTER" })
                {
                    RegisterZeroMqClient(message.Sender, identity);
                    return;
                }

                // Handle client unregistration
                if (message is { Type: MessageType.System, Content: "CLIENT_UNREGISTER" })
                {
                    UnregisterZeroMqClient(message.Sender);
                    return;
                }

                // Update client activity
                if (_clients.TryGetValue(message.Sender, out var clientInfo))
                {
                    clientInfo.LastActivity = DateTime.UtcNow;
                }

                HandleReceivedMessage(message, message.Sender);
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing ZeroMQ message: {ex.Message}", ex));
        }
    }

    private void RegisterZeroMqClient(string clientName, byte[] identity)
    {
        try
        {
            if (_clients.ContainsKey(clientName)) return;

            var clientInfo = new ZeroMqClientInfo(clientName, identity, $"zeromq://{_config?.Host}:{_config?.RouterPort}");
            _clients[clientName] = clientInfo;

            var connectionInfo = new ConnectionInfo
            {
                Id = clientName,
                Name = clientName,
                Address = clientInfo.Address,
                ConnectedAt = DateTime.UtcNow,
                IsActive = true,
                Properties = new Dictionary<string, object>
                {
                    ["Protocol"] = "ZeroMQ",
                    ["RouterPort"] = _config?.RouterPort ?? 0,
                    ["PublisherPort"] = _config?.PublisherPort ?? 0
                }
            };

            RegisterClient(clientName, clientName, clientInfo);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error registering ZeroMQ client: {ex.Message}", ex));
        }
    }

    private void UnregisterZeroMqClient(string clientName)
    {
        try
        {
            if (_clients.TryRemove(clientName, out var clientInfo))
            {
                UnregisterClient(clientName);
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error unregistering ZeroMQ client: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (!IsRunning) return false;

            if (string.IsNullOrEmpty(connectionId))
            {
                return await BroadcastMessageAsync(message);
            }

            if (_router == null || !_clients.TryGetValue(connectionId, out var clientInfo))
                return false;

            var json = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(json);

            _router.SendMoreFrame(clientInfo.Identity)
                   .SendFrame(messageBytes);

            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send ZeroMQ message: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            if (!IsRunning || _publisher == null) return false;

            var json = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(json);

            // Publish with topic
            _publisher.SendMoreFrame("broadcast")
                     .SendFrame(messageBytes);

            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast ZeroMQ message: {ex.Message}", ex));
            return false;
        }
    }

    protected override string GetClientAddress(object? transportSpecificData)
    {
        return transportSpecificData is ZeroMqClientInfo client
            ? client.Address
            : "zeromq://unknown";
    }
    #endregion

    #region Helper Classes
    internal class ZeroMqClientInfo(string clientName, byte[] identity, string address)
    {
        public string ClientName { get; } = clientName;
        public byte[] Identity { get; } = identity;
        public string Address { get; } = address;
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }
    #endregion
}