

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.WebSocket;

public class WebSocketMessageClient : MessageClientBase
{
    #region Fields

    private ClientWebSocket? _webSocket;
    private Task? _readTask;
    private CancellationTokenSource? _cancellationTokenSource;

    #endregion

    #region Properties

    public override bool IsConnected => _webSocket?.State == WebSocketState.Open;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.WebSocket;

    #endregion

    #region Methods

    /// <summary>
    /// Establishes a WebSocket connection to the specified server using the provided configuration settings.
    /// Closes any existing connection before initiating a new one.
    /// 
    /// Configuration dictionary keys:
    /// - "Host" (string, optional): The remote host to connect to. Defaults to "localhost".
    /// - "Port" (int, optional): The port number to connect to. Defaults to 8080.
    /// - "Path" (string, optional): The URI path to connect to. Defaults to "/".
    /// - "UseSSL" (bool, optional): Whether to use a secure WebSocket connection (wss). Defaults to false.
    /// - "Timeout" (int, optional): Connection timeout in milliseconds. Defaults to 5000.
    /// - "ClientName" (string, optional): A name used to identify the client. Defaults to the current user's name.
    /// 
    /// Builds the connection URI, creates and connects a ClientWebSocket, and begins listening for messages asynchronously.
    /// Triggers the Connected event on success or ErrorOccurred on failure.
    /// </summary>
    /// <param name="configuration">Dictionary containing WebSocket connection configuration options.</param>
    /// <returns>True if the connection is successfully established; otherwise, false.</returns>

    public override async Task<bool> ConnectAsync(Dictionary<string, object>? configuration)
    {
        try
        {
            await DisconnectAsync();

            var host = configuration?.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration?.GetValueOrDefault("Port", 8080) as int? ?? 8080;
            var path = configuration?.GetValueOrDefault("Path", "/") as string ?? "/";
            var useSsl = configuration?.GetValueOrDefault("UseSSL", false) as bool? ?? false;
            var timeout = configuration?.GetValueOrDefault("Timeout", 5000) as int? ?? 5000;
            var clientName = configuration?.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;

            var protocol = useSsl ? "wss" : "ws";
            var uri = new Uri($"{protocol}://{host}:{port}{path}");

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                _cancellationTokenSource.Token, timeoutCts.Token);

            await _webSocket.ConnectAsync(uri, combinedCts.Token);

            ConnectionInfo = new ConnectionInfo
            {
                Id = clientName,
                Name = clientName,
                Address = uri.ToString(),
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };

            _readTask = Task.Run(ReadMessagesAsync, _cancellationTokenSource.Token);

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
            OnErrorOccurred(new ErrorEventArgs($"Connection failed: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected && ConnectionInfo != null)
            {
                var unregisterMessage = new Message
                {
                    Content = "CLIENT_UNREGISTER",
                    Sender = ConnectionInfo.Name,
                    Receiver = "System",
                    Type = MessageType.System
                };
                await SendMessageAsync(unregisterMessage);
            }

            _cancellationTokenSource?.Cancel();

            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting",
                        CancellationToken.None);
                }
                catch
                {
                    /*ignored*/
                }
            }

            if (_readTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_readTask, Task.Delay(2000)) == _readTask)
                        await _readTask;
                }
                catch
                {
                    /*ignored*/
                }
                _readTask = null;
            }

            _webSocket?.Dispose();
            _webSocket = null;

            if (ConnectionInfo != null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Disconnection error: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _disposed) return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            if (_webSocket is not null && _cancellationTokenSource != null)
                await _webSocket.SendAsync(segment, WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    private async Task ReadMessagesAsync()
    {
        var buffer = new byte[4096];

        try
        {
            while (_cancellationTokenSource is { Token.IsCancellationRequested: false } &&
                   !_disposed &&
                   IsConnected)
            {
                if (_webSocket != null)
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
                                if (message != null && ConnectionInfo is not null)
                                    OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
                            }
                        }
                        catch (JsonException ex)
                        {
                            OnErrorOccurred(new ErrorEventArgs($"Message parse error: {ex.Message}", ex, ConnectionInfo));
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_cancellationTokenSource is { Token.IsCancellationRequested: false } && !_disposed)
            {
                OnErrorOccurred(new ErrorEventArgs($"Read error: {ex.Message}", ex, ConnectionInfo));
            }
        }
    }

    public override void Dispose()
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
    #endregion

}