

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Messaging.ModelLibrary.WebSocket;

public class WebSocketMessageClient : IMessageClient
{
    private ClientWebSocket? _webSocket;
    private Task? _readTask;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _disposed;

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;
    public ConnectionInfo ConnectionInfo { get; private set; }
    public TransportType TransportType => TransportType.WebSocket;

    public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var host = configuration?.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration?.GetValueOrDefault("Port", 8080) as int? ?? 8080;
            var path = configuration?.GetValueOrDefault("Path", "/") as string ?? "/";
            var useSSL = configuration?.GetValueOrDefault("UseSSL", false) as bool? ?? false;
            var timeout = configuration?.GetValueOrDefault("Timeout", 5000) as int? ?? 5000;
            var clientName = configuration?.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;

            var protocol = useSSL ? "wss" : "ws";
            var uri = new Uri($"{protocol}://{host}:{port}{path}");

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            // Set timeout for connection
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token, timeoutCts.Token);

            await _webSocket.ConnectAsync(uri, combinedCts.Token);

            ConnectionInfo = new ConnectionInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = clientName,
                Address = uri.ToString()
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

    public async Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
                }
                catch (Exception)
                {
                    // Ignore close errors
                }
            }

            if (_readTask != null)
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
                    // Ignore exceptions during task wait
                }
                _readTask = null;
            }

            _webSocket?.Dispose();
            _webSocket = null;

            if (ConnectionInfo != null)
            {
                Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
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
            if (!IsConnected || _disposed) return false;

            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
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
        catch (WebSocketException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    private async Task ReadMessagesAsync()
    {
        var buffer = new byte[4096];

        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested &&
                   !_disposed &&
                   IsConnected)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    try
                    {
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            var message = JsonSerializer.Deserialize<Message>(json);
                            MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
                        }
                    }
                    catch (JsonException ex)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex, ConnectionInfo));
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // WebSocket was disposed - this is expected during shutdown
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // Connection was closed - this is expected during disconnect
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested - this is expected during shutdown
        }
        catch (Exception ex)
        {
            if (!_cancellationTokenSource.Token.IsCancellationRequested && !_disposed)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Read error: {ex.Message}", ex, ConnectionInfo));
            }
        }
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