using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Messaging.ModelLibrary.WebSocket;

public class WebSocketTransport : IMessageTransport
{
    #region Fields

    private readonly ConcurrentDictionary<string, WebSocketClientConnection> _connections = new();

    private HttpListener? _httpListener;

    private CancellationTokenSource? _cancellationTokenSource = new CancellationTokenSource();

    private Task? _serverTask;

    private bool _disposed;

    private Dictionary<string, object> _baseConfiguration => new Dictionary<string, object>()
    {
        {"Host","localhost"}
        ,{"Port",8080}
        ,{"Path","/"}
    };

    #endregion

    #region Events

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? ClientConnected;
    public event EventHandler<ConnectionEventArgs>? ClientDisconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Properties

    public bool IsRunning { get; private set; }
    public TransportType TransportType => TransportType.WebSocket;
    public IReadOnlyList<ConnectionInfo> Connections { get; } = new List<ConnectionInfo>();


    #endregion


    #region Methods

    private async Task HandleWebSocketRequest(HttpListenerContext context)
    {
        try
        {
            WebSocketContext? webSocketContext = await context.AcceptWebSocketAsync(null);
            var webSocket = webSocketContext.WebSocket;

            var connectionInfo = new ConnectionInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Unknown",
                Address = context.Request.RemoteEndPoint?.ToString() ?? "Unknown"
            };

            var clientConnection = new WebSocketClientConnection(webSocket, connectionInfo, _cancellationTokenSource.Token);
            clientConnection.MessageReceived += OnClientMessageReceived;
            clientConnection.Disconnected += OnClientDisconnected;
            clientConnection.ErrorOccurred += OnClientErrorOccurred;

            _connections.TryAdd(connectionInfo.Id, clientConnection);
            ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));

            clientConnection.StartReading();
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"WebSocket handshake error: {ex.Message}", ex));

            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (Exception)
            {
                // Ignore response close errors
            }
        }
    }

    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        // Update connection name if this is the first message
        if (sender is WebSocketClientConnection connection && connection.Info.Name == "Unknown")
        {
            connection.Info.Name = e.Message.Sender;
        }

        MessageReceived?.Invoke(this, e);
    }

    private void OnClientDisconnected(object? sender, ConnectionEventArgs e)
    {
        _connections.TryRemove(e.Connection.Id, out _);
        ClientDisconnected?.Invoke(this, e);
    }

    private void OnClientErrorOccurred(object? sender, ErrorEventArgs e)
    {
        ErrorOccurred?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            try
            {
                StopAsync().Wait(3000); // Wait up to 3 seconds
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }
    }


    public async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();
            configuration ??= _baseConfiguration;
            var host = configuration.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration.GetValueOrDefault("Port", 8080) as int? ?? 8080;
            var path = configuration.GetValueOrDefault("Path", "/") as string ?? "/";
            _httpListener = new HttpListener();
            var prefix = $"http://{host}:{port}{path}";
            _httpListener.Prefixes.Add(prefix);
            _httpListener.Start();
            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(RunServerAsync, _cancellationTokenSource.Token);
            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start WebSocket server: {ex.Message}", ex));
            return false;
        }
    }

    private async Task RunServerAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    _ = Task.Run(async () => await HandleWebSocketRequest(context), _cancellationTokenSource.Token);
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
            catch (ObjectDisposedException)
            {
                // Listener was disposed - this is expected during shutdown
                break;
            }
            catch (HttpListenerException)
            {
                // Listener was stopped - this is expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Server error: {ex.Message}", ex));
                }
            }
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_serverTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_serverTask, Task.Delay(3000)) == _serverTask)
                    {
                        await _serverTask;
                    }
                }
                catch (Exception)
                {
                    // Ignore server task exceptions during shutdown
                }
                _serverTask = null;
            }

            try
            {
                _httpListener?.Stop();
            }
            catch (Exception)
            {
                // Ignore listener stop errors
            }

            _httpListener?.Close();
            _httpListener = null;

            // Dispose all connections
            var connectionTasks = _connections.Values.Select(connection => Task.Run(() => connection.Dispose()));
            try
            {
                await Task.WhenAll(connectionTasks);
            }
            catch (Exception)
            {
                // Ignore connection disposal errors
            }

            _connections.Clear();
            IsRunning = false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Error stopping server: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(Message message, string connectionId = null)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionId))
            {
                return await BroadcastMessageAsync(message);
            }

            if (_connections.TryGetValue(connectionId, out var connection))
            {
                return await connection.SendMessageAsync(message);
            }

            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send message: {ex.Message}", ex));
            return false;
        }
    }

    public async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            var tasks = _connections.Values.Select(connection => connection.SendMessageAsync(message));
            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to broadcast message: {ex.Message}", ex));
            return false;
        }
    }

    #endregion


    private class WebSocketClientConnection : IDisposable
    {
        private readonly System.Net.WebSockets.WebSocket _webSocket;
        private readonly CancellationToken _cancellationToken;
        private Task _readTask;
        private bool _disposed;

        public WebSocketClientConnection(System.Net.WebSockets.WebSocket webSocket,
            ConnectionInfo info, CancellationToken cancellationToken)
        {
            _webSocket = webSocket;
            Info = info;
            _cancellationToken = cancellationToken;
        }

        public ConnectionInfo Info { get; }

        public event EventHandler<MessageEventArgs> MessageReceived;
        public event EventHandler<ConnectionEventArgs> Disconnected;
        public event EventHandler<ErrorEventArgs> ErrorOccurred;

        public void StartReading()
        {
            _readTask = Task.Run(ReadMessagesAsync, _cancellationToken);
        }

        public async Task<bool> SendMessageAsync(Message message)
        {
            try
            {
                if (_disposed || _webSocket.State != WebSocketState.Open)
                    return false;

                var json = JsonSerializer.Serialize(message);
                var buffer = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(buffer);

                await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, _cancellationToken);
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
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, Info));
                return false;
            }
        }

        private async Task ReadMessagesAsync()
        {
            var buffer = new byte[4096];

            try
            {
                while (!_cancellationToken.IsCancellationRequested &&
                       !_disposed &&
                       _webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        try
                        {
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                var message = JsonSerializer.Deserialize<Message>(json);
                                MessageReceived?.Invoke(this, new MessageEventArgs(message, Info));
                            }
                        }
                        catch (JsonException ex)
                        {
                            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex, Info));
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
                if (!_cancellationToken.IsCancellationRequested && !_disposed)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Read error: {ex.Message}", ex, Info));
                }
            }
            finally
            {
                if (!_disposed)
                {
                    Disconnected?.Invoke(this, new ConnectionEventArgs(Info));
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
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutdown", CancellationToken.None).Wait(1000);
                    }
                }
                catch (Exception)
                {
                    // Ignore close errors
                }

                try
                {
                    _webSocket?.Dispose();
                }
                catch (Exception)
                {
                    // Ignore disposal errors
                }

                try
                {
                    _readTask?.Wait(1000); // Wait up to 1 second for read task to complete
                }
                catch (Exception)
                {
                    // Ignore task wait errors
                }
            }
        }
    }
}