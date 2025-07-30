using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Configuration;
using NetMQ;
using NetMQ.Sockets;
using System.Text;
using System.Text.Json;

namespace Messaging.ModelLibrary.ZeroMQ;
public class ZeroMqClient : MessageClientBase
{
    #region Fields
    private DealerSocket? _dealer;
    private SubscriberSocket? _subscriber;
    private NetMQPoller? _poller;
    private ZeroMqConfig? _config;
    private volatile bool _running;
    #endregion

    #region Properties
    public override bool IsConnected => _dealer != null && _subscriber != null && _running;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.ZeroMQ;
    #endregion

    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            _config = ZeroMqConfig.FromDictionary(configuration);
            var clientName = _config.ClientName;

            // Create dealer socket for sending/receiving direct messages
            _dealer = new DealerSocket();
            _dealer.Options.Identity = Encoding.UTF8.GetBytes(clientName);
            _dealer.Connect($"tcp://{_config.Host}:{_config.RouterPort}");

            // Create subscriber socket for broadcast messages
            _subscriber = new SubscriberSocket();
            _subscriber.Connect($"tcp://{_config.Host}:{_config.PublisherPort}");
            _subscriber.Subscribe("broadcast");

            // Setup message handling
            _dealer.ReceiveReady += OnDealerReceiveReady;
            _subscriber.ReceiveReady += OnSubscriberReceiveReady;

            // Start poller
            _poller = new NetMQPoller { _dealer, _subscriber };
            _running = true;

            Task.Run(() => _poller.Run());

            ConnectionInfo = new ConnectionInfo
            {
                Id = clientName,
                Name = clientName,
                Address = $"{_config.Host}:{_config.RouterPort}",
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };

            // Send registration message
            var registrationMessage = new Message
            {
                Content = "CLIENT_REGISTER",
                Sender = clientName,
                Receiver = "System",
                Type = MessageType.System
            };
            await SendMessageAsync(registrationMessage);

            OnConnected(new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"ZeroMQ connection failed: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            _running = false;

            _poller?.Stop();
            _poller?.Dispose();
            _poller = null;

            _dealer?.Close();
            _dealer?.Dispose();
            _dealer = null;

            _subscriber?.Close();
            _subscriber?.Dispose();
            _subscriber = null;

            if (ConnectionInfo != null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"ZeroMQ disconnection error: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _dealer == null) return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var json = JsonSerializer.Serialize(message);
            var messageBytes = Encoding.UTF8.GetBytes(json);

            _dealer.SendFrame(messageBytes);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"ZeroMQ send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    private void OnDealerReceiveReady(object? sender, NetMQSocketEventArgs e)
    {
        try
        {
            if (!_running || _dealer == null) return;

            var messageBytes = _dealer.ReceiveFrameBytes();
            var json = Encoding.UTF8.GetString(messageBytes);
            var message = JsonSerializer.Deserialize<Message>(json);

            if (message != null && ConnectionInfo != null)
            {
                base.OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing dealer message: {ex.Message}", ex, ConnectionInfo));
        }
    }

    private void OnSubscriberReceiveReady(object? sender, NetMQSocketEventArgs e)
    {
        try
        {
            if (!_running || _subscriber == null) return;

            // Receive topic frame
            var topic = _subscriber.ReceiveFrameString();

            // Receive message frame
            var messageBytes = _subscriber.ReceiveFrameBytes();
            var json = Encoding.UTF8.GetString(messageBytes);
            var message = JsonSerializer.Deserialize<Message>(json);

            if (message != null && ConnectionInfo != null)
            {
                // Don't process our own broadcast messages
                if (message.Sender == ConnectionInfo.Name)
                    return;

                base.OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing subscriber message: {ex.Message}", ex, ConnectionInfo));
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            base.Dispose();
            try
            {
                DisconnectAsync().Wait(3000);
            }
            catch { /* ignore */ }
        }
    }
}
