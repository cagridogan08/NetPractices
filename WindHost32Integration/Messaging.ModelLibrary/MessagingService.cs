namespace Messaging.ModelLibrary;

public class MessagingService : IMessagingService
{
    private IMessageTransport _transport;
    private IMessageClient _client;
    private bool _disposed;

    public event EventHandler<MessageEventArgs> MessageReceived;
    public event EventHandler<ConnectionEventArgs> Connected;
    public event EventHandler<ConnectionEventArgs> Disconnected;
    public event EventHandler<ErrorEventArgs> ErrorOccurred;

    public bool IsRunning => _transport?.IsRunning == true || _client?.IsConnected == true;
    public MessagingMode Mode { get; private set; } = MessagingMode.None;
    public string ClientName { get; set; } = Environment.UserName;
    public IReadOnlyList<ConnectionInfo> Connections => _transport?.Connections ?? new List<ConnectionInfo>();

    public async Task<bool> StartServerAsync(IMessageTransport transport, Dictionary<string, object> configuration = null)
    {
        try
        {
            await StopAsync();

            _transport = transport;
            _transport.MessageReceived += OnTransportMessageReceived;
            _transport.ClientConnected += OnTransportClientConnected;
            _transport.ClientDisconnected += OnTransportClientDisconnected;
            _transport.ErrorOccurred += OnTransportErrorOccurred;

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

            Mode = MessagingMode.None;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Error during stop: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(string content, string receiver = null, MessageType type = MessageType.Text)
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
            if (message.Sender == string.Empty)
                message.Sender = ClientName;

            if (Mode == MessagingMode.Server && _transport != null)
            {
                if (string.IsNullOrEmpty(message.Receiver))
                {
                    return await _transport.BroadcastMessageAsync(message);
                }
                else
                {
                    var connection = Connections.FirstOrDefault(c => c.Id.Equals(message.Receiver));
                    return await _transport.SendMessageAsync(message, connection?.Id);
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

    private void OnTransportMessageReceived(object sender, MessageEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnTransportClientConnected(object sender, ConnectionEventArgs e)
    {
        Connected?.Invoke(this, e);
    }

    private void OnTransportClientDisconnected(object sender, ConnectionEventArgs e)
    {
        Disconnected?.Invoke(this, e);
    }

    private void OnTransportErrorOccurred(object sender, ErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, e);
    }

    private void OnClientMessageReceived(object sender, MessageEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnClientConnected(object sender, ConnectionEventArgs e)
    {
        Connected?.Invoke(this, e);
    }

    private void OnClientDisconnected(object sender, ConnectionEventArgs e)
    {
        Disconnected?.Invoke(this, e);
    }

    private void OnClientErrorOccurred(object sender, ErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopAsync().Wait();
            _transport?.Dispose();
            _client?.Dispose();
            _disposed = true;
        }
    }
}