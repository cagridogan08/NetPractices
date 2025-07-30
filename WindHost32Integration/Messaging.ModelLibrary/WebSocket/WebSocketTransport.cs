using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.WebSocket;

public class WebSocketTransport : MessageTransportBase
{
    #region Fields
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    #endregion

    #region Properties
    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.WebSocket;
    #endregion

    /// <summary>
    /// Starts the WebSocket server with the provided configuration parameters.
    /// </summary>
    /// <param name="configuration">
    /// Optional configuration dictionary containing settings such as:
    /// <list type="bullet">
    /// <item><description><c>"Host"</c> (string): The hostname or IP address to bind. Default is <c>"localhost"</c>.</description></item>
    /// <item><description><c>"Port"</c> (int): The port number to listen on. Default is <c>8080</c>.</description></item>
    /// <item><description><c>"Path"</c> (string): The URL path prefix. Default is <c>"/"</c>.</description></item>
    /// </list>
    /// </param>
    /// <returns>
    /// Returns <c>true</c> if the server starts successfully; otherwise, <c>false</c>.
    /// </returns>
    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            var host = configuration?.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration?.GetValueOrDefault("Port", 8080) as int? ?? 8080;
            var path = configuration?.GetValueOrDefault("Path", "/") as string ?? "/";

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
            OnErrorOccurred(new ErrorEventArgs($"Failed to start WebSocket server: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_serverTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_serverTask, Task.Delay(3000)) == _serverTask)
                        await _serverTask;
                }
                catch {/*ignored*/ }
                _serverTask = null;
            }

            try
            {
                _httpListener?.Stop();
            }
            catch { /*ignored*/}

            _httpListener?.Close();
            _httpListener = null;

            // Dispose all connections
            var connectionTasks = _connections.Values.Select(connection =>
                Task.Run(() => (connection.TransportData as WebSocketClientConnection)?.Dispose()));

            try
            {
                await Task.WhenAll(connectionTasks);
            }
            catch {/*ignored*/ }

            _connections.Clear();
            IsRunning = false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error stopping server: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionId))
                return await BroadcastMessageAsync(message);

            if (_connections.TryGetValue(connectionId, out var clientInfo) &&
                clientInfo.TransportData is WebSocketClientConnection connection)
            {
                return await connection.SendMessageAsync(message);
            }

            return false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send message: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            var tasks = _connections.Values
                .Where(c => c.TransportData is WebSocketClientConnection)
                .Select(c => ((WebSocketClientConnection)c.TransportData).SendMessageAsync(message));

            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast message: {ex.Message}", ex));
            return false;
        }
    }

    protected override string GetClientAddress(object transportSpecificData)
    {
        return transportSpecificData is WebSocketClientConnection conn
            ? conn.RemoteAddress
            : "Unknown";
    }

    private async Task RunServerAsync()
    {
        while (_cancellationTokenSource is { Token.IsCancellationRequested: false })
        {
            try
            {
                if (_httpListener != null)
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
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    OnErrorOccurred(new ErrorEventArgs($"Server error: {ex.Message}", ex));
                }
            }
        }
    }

    private async Task HandleWebSocketRequest(HttpListenerContext context)
    {
        try
        {
            WebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null);
            var webSocket = webSocketContext.WebSocket;
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            var remoteAddress = context.Request.RemoteEndPoint?.ToString() ?? "Unknown";

            if (_cancellationTokenSource != null)
            {
                var clientConnection = new WebSocketClientConnection(webSocket, remoteAddress, _cancellationTokenSource.Token);
                clientConnection.MessageReceived += OnClientMessageReceived;
                clientConnection.Disconnected += OnClientDisconnected;
                clientConnection.ErrorOccurred += OnClientErrorOccurred;

                clientConnection.StartReading();
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"WebSocket handshake error: {ex.Message}", ex));

            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch {/*ignored*/ }
        }
    }

    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        if (sender is WebSocketClientConnection connection)
        {
            // Register client if this is a registration message
            if (e.Message is { Type: MessageType.System, Content: "CLIENT_REGISTER" })
            {
                RegisterClient(e.Message.Sender, e.Message.Sender, connection);
                return;
            }

            HandleReceivedMessage(e.Message, e.Message.Sender);
        }
    }

    private void OnClientDisconnected(object? sender, ConnectionEventArgs e)
    {
        UnregisterClient(e.Connection.Id);
    }

    private void OnClientErrorOccurred(object? sender, ErrorEventArgs e)
    {
        OnErrorOccurred(e);
    }

    #region Enhanced WebSocket Client Connection
    private class WebSocketClientConnection(
        System.Net.WebSockets.WebSocket webSocket,
        string remoteAddress,
        CancellationToken cancellationToken)
        : IDisposable
    {
        private Task? _readTask;
        private bool _disposed;

        public string RemoteAddress { get; } = remoteAddress;

        public event EventHandler<MessageEventArgs>? MessageReceived;
        public event EventHandler<ConnectionEventArgs>? Disconnected;
        public event EventHandler<ErrorEventArgs>? ErrorOccurred;

        public void StartReading()
        {
            _readTask = Task.Run(ReadMessagesAsync, cancellationToken);
        }

        public async Task<bool> SendMessageAsync(Message message)
        {
            try
            {
                if (_disposed || webSocket.State != WebSocketState.Open)
                    return false;

                var json = JsonSerializer.Serialize(message);
                var buffer = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(buffer);

                await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex));
                return false;
            }
        }

        private async Task ReadMessagesAsync()
        {
            var buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested &&
                       !_disposed &&
                       webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);

                        try
                        {
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                var message = JsonSerializer.Deserialize<Message>(json);
                                if (message != null)
                                {
                                    var connectionInfo = new ConnectionInfo
                                    {
                                        Id = message.Sender,
                                        Name = message.Sender,
                                        Address = RemoteAddress
                                    };
                                    MessageReceived?.Invoke(this, new MessageEventArgs(message, connectionInfo));
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex));
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested && !_disposed)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Read error: {ex.Message}", ex));
                }
            }
            finally
            {
                if (!_disposed)
                {
                    var connectionInfo = new ConnectionInfo
                    {
                        Id = "Unknown",
                        Name = "Unknown",
                        Address = RemoteAddress
                    };
                    Disconnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
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
                    if (webSocket.State == WebSocketState.Open)
                    {
                        webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutdown", CancellationToken.None).Wait(1000);
                    }
                }
                catch {/*ignored*/ }

                try
                {
                    webSocket?.Dispose();
                }
                catch {/*ignored*/ }

                try
                {
                    _readTask?.Wait(1000);
                }
                catch {/*ignored*/ }
            }
        }
    }
    #endregion
}