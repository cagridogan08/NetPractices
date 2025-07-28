using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Broker;

namespace Messaging.ModelLibrary;

public class MessagingService : IMessagingService
{
    private IMessageTransport? _transport;
    private IMessageClient? _client;
    private MessageBroker? _broker;
    private bool _disposed;

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    public bool IsRunning => _transport?.IsRunning == true || _client?.IsConnected == true;
    public MessagingMode Mode { get; private set; } = MessagingMode.None;
    public string ClientName { get; set; } = Environment.UserName;
    public IReadOnlyList<ConnectionInfo> Connections => _transport?.Connections ?? new List<ConnectionInfo>();

    // Enhanced properties
    public MessageBroker? Broker => _broker;
    public IReadOnlyList<ClientInfo> OnlineClients => _broker?.GetOnlineClientsAsync().Result ?? new List<ClientInfo>();

    public async Task<bool> StartServerAsync(IMessageTransport transport, Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            _transport = transport;
            _broker = new MessageBroker();

            // Register transport with broker
            _broker.RegisterTransport(_transport);

            // Subscribe to events
            _transport.MessageReceived += OnTransportMessageReceived;
            _transport.ClientConnected += OnTransportClientConnected;
            _transport.ClientDisconnected += OnTransportClientDisconnected;
            _transport.ErrorOccurred += OnTransportErrorOccurred;

            _broker.MessageRouted += OnBrokerMessageRouted;
            _broker.ClientDiscovered += OnBrokerClientDiscovered;
            _broker.ClientDisconnected += OnBrokerClientDisconnected;
            _broker.ErrorOccurred += OnBrokerErrorOccurred;

            var success = await _transport.StartAsync(configuration);
            if (success)
            {
                Mode = MessagingMode.Server;
            }

            return success;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start server: {ex.Message}", ex));
            return false;
        }
    }

    public async Task<bool> ConnectAsClientAsync(IMessageClient client, Dictionary<string, object> configuration)
    {
        try
        {
            await StopAsync();

            _client = client;

            // Subscribe to events
            _client.MessageReceived += OnClientMessageReceived;
            _client.Connected += OnClientConnected;
            _client.Disconnected += OnClientDisconnected;
            _client.ErrorOccurred += OnClientErrorOccurred;

            var success = await _client.ConnectAsync(configuration);
            if (success)
            {
                Mode = MessagingMode.Client;
            }

            return success;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to connect as client: {ex.Message}", ex));
            return false;
        }
    }

    // Enhanced methods for client-to-client messaging
    public async Task<bool> SendDirectMessageAsync(string recipientId, string content, MessageType type = MessageType.Text)
    {
        var message = new Message
        {
            Content = content,
            Sender = ClientName,
            Receiver = recipientId,
            Type = type
        };

        return await SendMessageAsync(message);
    }

    public async Task<bool> SendGroupMessageAsync(string groupName, string content)
    {
        var message = new Message
        {
            Content = content,
            Sender = ClientName,
            Receiver = $"group:{groupName}",
            Type = MessageType.Text
        };

        return await SendMessageAsync(message);
    }

    public async Task<IReadOnlyList<ClientInfo>> GetOnlineClientsAsync()
    {
        if (_broker != null)
            return await _broker.GetOnlineClientsAsync();

        if (_client is MessageClientBase enhancedClient)
            return await enhancedClient.GetOnlineClientsAsync();

        return new List<ClientInfo>();
    }

    public async Task<bool> CreateGroupAsync(string groupName)
    {
        if (_broker != null)
            return await _broker.CreateGroupAsync(groupName, ClientName);

        if (_client is MessageClientBase enhancedClient)
            return await enhancedClient.CreateGroupAsync(groupName);

        return false;
    }

    public async Task<bool> JoinGroupAsync(string groupName)
    {
        if (_broker != null)
            return await _broker.JoinGroupAsync(groupName, ClientName);

        if (_client is MessageClientBase enhancedClient)
            return await enhancedClient.JoinGroupAsync(groupName);

        return false;
    }

    public async Task<bool> LeaveGroupAsync(string groupName)
    {
        if (_broker != null)
            return await _broker.LeaveGroupAsync(groupName, ClientName);

        if (_client is MessageClientBase enhancedClient)
            return await enhancedClient.LeaveGroupAsync(groupName);

        return false;
    }

    // Existing methods remain the same
    public async Task StopAsync()
    {
        try
        {
            if (_transport != null)
            {
                _transport.MessageReceived -= OnTransportMessageReceived;
                _transport.ClientConnected -= OnTransportClientConnected;
                _transport.ClientDisconnected -= OnTransportClientDisconnected;
                _transport.ErrorOccurred -= OnTransportErrorOccurred;

                if (_broker != null)
                {
                    _broker.UnregisterTransport(_transport);
                }

                await _transport.StopAsync();
                _transport = null;
            }

            if (_client != null)
            {
                _client.MessageReceived -= OnClientMessageReceived;
                _client.Connected -= OnClientConnected;
                _client.Disconnected -= OnClientDisconnected;
                _client.ErrorOccurred -= OnClientErrorOccurred;
                await _client.DisconnectAsync();
                _client = null;
            }

            _broker?.Dispose();
            _broker = null;

            Mode = MessagingMode.None;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Error during stop: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(string content, string? receiver = null, MessageType type = MessageType.Text)
    {
        var message = new Message
        {
            Content = content,
            Sender = ClientName,
            Receiver = receiver,
            Type = type
        };

        return await SendMessageAsync(message);
    }

    public async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ClientName;

            if (Mode == MessagingMode.Server && _transport != null)
            {
                if (string.IsNullOrEmpty(message.Receiver))
                {
                    return await _transport.BroadcastMessageAsync(message);
                }
                else
                {
                    return await _transport.SendMessageAsync(message, message.Receiver);
                }
            }
            else if (Mode == MessagingMode.Client && _client != null)
            {
                return await _client.SendMessageAsync(message);
            }

            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send message: {ex.Message}", ex));
            return false;
        }
    }

    public async Task<bool> BroadcastMessageAsync(string content, MessageType type = MessageType.Text)
    {
        if (Mode != MessagingMode.Server || _transport == null)
            return false;

        var message = new Message
        {
            Content = content,
            Sender = ClientName,
            Type = type
        };

        return await _transport.BroadcastMessageAsync(message);
    }

    #region Event Handlers
    private void OnTransportMessageReceived(object? sender, MessageEventArgs e) => MessageReceived?.Invoke(this, e);
    private void OnTransportClientConnected(object? sender, ConnectionEventArgs e) => Connected?.Invoke(this, e);
    private void OnTransportClientDisconnected(object? sender, ConnectionEventArgs e) => Disconnected?.Invoke(this, e);
    private void OnTransportErrorOccurred(object? sender, ErrorEventArgs e) => ErrorOccurred?.Invoke(this, e);

    private void OnClientMessageReceived(object? sender, MessageEventArgs e) => MessageReceived?.Invoke(this, e);
    private void OnClientConnected(object? sender, ConnectionEventArgs e) => Connected?.Invoke(this, e);
    private void OnClientDisconnected(object? sender, ConnectionEventArgs e) => Disconnected?.Invoke(this, e);
    private void OnClientErrorOccurred(object? sender, ErrorEventArgs e) => ErrorOccurred?.Invoke(this, e);

    private void OnBrokerMessageRouted(object? sender, MessageEventArgs e) => MessageReceived?.Invoke(this, e);
    private void OnBrokerClientDiscovered(object? sender, ClientDiscoveryEventArgs e) { /* Handle as needed */ }
    private void OnBrokerClientDisconnected(object? sender, ClientDiscoveryEventArgs e) { /* Handle as needed */ }
    private void OnBrokerErrorOccurred(object? sender, ErrorEventArgs e) => ErrorOccurred?.Invoke(this, e);
    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopAsync().Wait();
            }
            catch { }

            _transport?.Dispose();
            _client?.Dispose();
            _broker?.Dispose();
            _disposed = true;
        }
    }
}