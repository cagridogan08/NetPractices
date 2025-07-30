

using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Configuration;
using NetMQ;
using NetMQ.Sockets;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Messaging.ModelLibrary.ZeroMQ;
internal class ZeroMqTransport : MessageTransportBase
{
    #region Fields
    private PublisherSocket? _publisher;
    private RouterSocket? _router;
    private NetMQPoller? _poller;
    private readonly ConcurrentDictionary<string, ZeroMqClientInfo> _clients = new();
    private ZeroMqConfig? _config;
    private volatile bool _running;
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

            _publisher = new PublisherSocket();
            _publisher.Bind($"tcp://*{_config.PublisherPort}");
            _router = new RouterSocket();
            _router.Bind($"tcp://*:{_config.RouterPort}");
            _router.ReceiveReady += OnRouterReceiveReady;
            _poller = new NetMQPoller { _router };
            _running = true;

            await Task.Run(() => _poller.Run());

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to start ZeroMQ transport: {ex.Message}", ex));
        }

        return false;
    }
    public override Task StopAsync()
    {
        try
        {
            _running = false;
            IsRunning = false;

            _poller?.Stop();
            _poller?.Dispose();
            _poller = null;

            _publisher?.Close();
            _publisher?.Dispose();
            _publisher = null;

            _router?.Close();
            _router?.Dispose();
            _router = null;

            _clients.Clear();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error stopping ZeroMQ transport: {ex.Message}", ex));
        }
        return Task.CompletedTask;
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

            var clientInfo = new ZeroMqClientInfo(clientName, identity, $"zeromq://{_config?.RouterPort}");
            _clients[clientName] = clientInfo;

            var connectionInfo = new ConnectionInfo
            {
                Id = clientName,
                Name = clientName,
                Address = clientInfo.Address,
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };

            RegisterClient(clientName, clientName, clientInfo);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error registering ZeroMQ client: {ex.Message}", ex));
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
            if (_router == null || !_clients.TryGetValue(connectionId, out var clientInfo)) return false;
            var json = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(json);
            _router.SendMoreFrame(clientInfo.Identity)
                .SendFrame(messageBytes);

            return true;

        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send ZeroMQ message :{e.Message}", e));
        }

        return false;
    }
    protected override string GetClientAddress(object? transportSpecificData)
    {
        return transportSpecificData is ZeroMqClientInfo client
            ? client.Address
            : "zeromq://unknown";
    }

    public override Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            if (!IsRunning || _publisher == null) return Task.FromResult(false);

            var json = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(json);

            // Publish with topic
            _publisher.SendMoreFrame("broadcast")
                .SendFrame(messageBytes);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast ZeroMQ message: {ex.Message}", ex));
        }
        return Task.FromResult(false);
    }

    #endregion

    #region ClientInfoModel

    /// <summary>
    /// ZeroMQ client information container
    /// </summary>
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
