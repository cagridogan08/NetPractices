namespace Messaging.ModelLibrary.InMemory;

public class InMemoryClient : IMessageClient
{
    private InMemoryTransport.InMemoryClientConnection _connection;
    private bool _disposed;
    private readonly string _clientName;

    public InMemoryClient(string clientName = null)
    {
        _clientName = clientName ?? Environment.UserName;
    }

    public event EventHandler<MessageEventArgs> MessageReceived;
    public event EventHandler<ConnectionEventArgs> Connected;
    public event EventHandler<ConnectionEventArgs> Disconnected;
    public event EventHandler<ErrorEventArgs> ErrorOccurred;

    public bool IsConnected => _connection != null;
    public ConnectionInfo ConnectionInfo => _connection?.Info;
    public TransportType TransportType => TransportType.InMemory;
    public string ClientName => _clientName;

    public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var serverName = configuration?.GetValueOrDefault("ServerName", "default") as string ?? "default";
            var server = InMemoryTransport.GetServer(serverName);

            if (server == null || !server.IsRunning)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"InMemory server '{serverName}' not found or not running"));
                return false;
            }

            var success = server.RegisterClient(this);
            if (success && _connection != null)
            {
                _connection.StartReading();
                Connected?.Invoke(this, new ConnectionEventArgs(_connection.Info));
            }

            return success;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Connection failed: {ex.Message}", ex));
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (_connection != null)
            {
                var connectionInfo = _connection.Info;
                _connection.Disconnect();
                _connection = null;

                Disconnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Disconnection error: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (_connection == null || _disposed) return false;

            _connection.ReceiveFromClient(message);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    internal void SetConnection(InMemoryTransport.InMemoryClientConnection connection)
    {
        _connection = connection;
    }

    internal void OnMessageReceived(Message message, ConnectionInfo connectionInfo)
    {
        MessageReceived?.Invoke(this, new MessageEventArgs(message, connectionInfo));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            try
            {
                DisconnectAsync().Wait(2000); // Wait up to 2 seconds
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }
    }
}