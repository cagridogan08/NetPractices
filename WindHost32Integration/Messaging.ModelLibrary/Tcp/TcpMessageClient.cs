
using System.Net.Sockets;
using System.Text.Json;

namespace Messaging.ModelLibrary.Tcp;

public class TcpMessageClient : IMessageClient
{
    #region Fields

    private TcpClient? _tcpClient;

    private NetworkStream? _networkStream;

    private Task? _readTask;

    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    #endregion

    #region Events

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Properties

    public bool IsConnected => _tcpClient?.Connected ?? false;
    public ConnectionInfo? ConnectionInfo { get; private set; }
    public TransportType TransportType => TransportType.Tcp;

    #endregion

    #region Methods

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

    public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var host = configuration?.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration?.GetValueOrDefault("Port", 8080) as int? ?? 8080;
            var timeout = configuration?.GetValueOrDefault("Timeout", 5000) as int? ?? 5000;
            var clientName = configuration?.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;

            _cancellationTokenSource = new CancellationTokenSource();

            _tcpClient = new TcpClient();
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token, timeoutCts.Token);

            await _tcpClient.ConnectAsync(host, port, combinedCts.Token);
            _networkStream = _tcpClient.GetStream();

            ConnectionInfo = new ConnectionInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = clientName,
                Address = $"{host}:{port}"
            };

            _readTask = Task.Run(ReadMessagesAsync, _cancellationTokenSource.Token);

            Connected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Connection failed: {ex.Message}", ex));
            return false;
        }
    }

    private async Task? ReadMessagesAsync()
    {
        try
        {
            if (_networkStream == null) return;
            using var reader = new StreamReader(_networkStream, System.Text.Encoding.UTF8);
            while (_cancellationTokenSource is { Token.IsCancellationRequested: false } &&
                   !_disposed &&
                   IsConnected &&
                   await reader.ReadLineAsync() is { } json)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var message = JsonSerializer.Deserialize<Message>(json);
                    if (message != null && ConnectionInfo != null)
                        MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
                }
                catch (JsonException ex)
                {
                    if (ConnectionInfo != null)
                        ErrorOccurred?.Invoke(this,
                            new ErrorEventArgs($"Message parse error: {ex.Message}", ex, ConnectionInfo));
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Stream was disposed - this is expected during shutdown
        }
        catch (InvalidOperationException)
        {
            // Stream is closed - this is expected during disconnect
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested - this is expected during shutdown
        }
        catch (Exception ex)
        {
            if (_cancellationTokenSource is { Token.IsCancellationRequested: false } && !_disposed && ConnectionInfo is not null)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Read error: {ex.Message}", ex, ConnectionInfo));
            }
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_readTask is not null)
            {
                try
                {
                    if (await Task.WhenAny(_readTask, Task.Delay(2000)) == _readTask)
                    {
                        await _readTask;
                    }
                }
                catch (Exception)
                {
                    /*Ignore*/
                }
            }

            try
            {
                _networkStream?.Close();
                _tcpClient?.Close();
            }
            catch (Exception)
            {
                // Ignore close errors
            }

            _networkStream?.Dispose();
            _tcpClient?.Dispose();
            _networkStream = null;
            _tcpClient = null;

            if (ConnectionInfo != null)
            {
                Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception e)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Disconnection error: {e.Message}", e));
        }
    }

    public async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _disposed || _networkStream == null) return false;

            var json = JsonSerializer.Serialize(message);
            var data = System.Text.Encoding.UTF8.GetBytes(json + "\n");

            if (_cancellationTokenSource != null)
                await _networkStream.WriteAsync(data, _cancellationTokenSource.Token);
            await _networkStream.FlushAsync();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    #endregion

}