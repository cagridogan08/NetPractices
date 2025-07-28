
using System.Net.Sockets;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Tcp;

public class TcpMessageClient : MessageClientBase
{
    #region Fields
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private Task? _readTask;
    private CancellationTokenSource? _cancellationTokenSource;
    #endregion

    #region Properties
    public override bool IsConnected => _tcpClient?.Connected ?? false;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.Tcp;
    #endregion

    public override async Task<bool> ConnectAsync(Dictionary<string, object>? configuration)
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
                Id = clientName,
                Name = clientName,
                Address = $"{host}:{port}",
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
            // Send unregister message
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

            if (_readTask is not null)
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
            }

            try
            {
                _networkStream?.Close();
                _tcpClient?.Close();
            }
            catch
            {
                /*ignored*/
            }

            _networkStream?.Dispose();
            _tcpClient?.Dispose();
            _networkStream = null;
            _tcpClient = null;

            if (ConnectionInfo != null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Disconnection error: {e.Message}", e));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _disposed || _networkStream == null) return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var json = JsonSerializer.Serialize(message);
            var data = System.Text.Encoding.UTF8.GetBytes(json + "\n");

            if (_cancellationTokenSource != null)
                await _networkStream.WriteAsync(data, _cancellationTokenSource.Token);
            await _networkStream.FlushAsync();
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
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
                        OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
                }
                catch (JsonException ex)
                {
                    if (ConnectionInfo != null)
                        OnErrorOccurred(new ErrorEventArgs($"Message parse error: {ex.Message}", ex, ConnectionInfo));
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_cancellationTokenSource is { Token.IsCancellationRequested: false } && !_disposed && ConnectionInfo is not null)
            {
                OnErrorOccurred(new ErrorEventArgs($"Read error: {ex.Message}", ex, ConnectionInfo));
            }
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            base.Dispose();
            try
            {
                DisconnectAsync().Wait(2000);
            }
            catch
            {
                /*ignored*/
            }
        }
    }
}
