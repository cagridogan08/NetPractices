using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Tcp;

public class TcpTransport : MessageTransportBase
{
    #region Fields
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    #endregion

    #region Properties
    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.Tcp;
    #endregion

    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            var host = configuration?.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration?.GetValueOrDefault("Port", 8080) as int? ?? 8080;

            var ipAddress = host == "localhost" ? IPAddress.Loopback : IPAddress.Parse(host);
            _listener = new TcpListener(ipAddress, port);
            _listener.Start();

            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(RunServerAsync, _cancellationTokenSource.Token);

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to start TCP server: {ex.Message}", ex));
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
                catch
                {
                    /*ignored*/
                }
                _serverTask = null;
            }

            try
            {
                _listener?.Stop();
            }
            catch {  /*ignored*/}

            // Dispose all connections
            var connectionTasks = _connections.Values.Select(connection =>
                Task.Run(() => (connection.TransportData as TcpClientConnection)?.Dispose()));

            try
            {
                await Task.WhenAll(connectionTasks);
            }
            catch { /*ignored*/ }

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
                clientInfo.TransportData is TcpClientConnection connection)
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
                .Where(c => c.TransportData is TcpClientConnection)
                .Select(c => ((TcpClientConnection)c.TransportData).SendMessageAsync(message));

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
        return transportSpecificData is TcpClientConnection conn
            ? conn.RemoteAddress
            : "Unknown";
    }

    private async Task RunServerAsync()
    {
        while (_cancellationTokenSource is { Token.IsCancellationRequested: false })
        {
            try
            {
                if (_listener != null)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();
                    var remoteAddress = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                    var clientConnection = new TcpClientConnection(tcpClient, remoteAddress, _cancellationTokenSource.Token);
                    clientConnection.MessageReceived += OnClientMessageReceived;
                    clientConnection.Disconnected += OnClientDisconnected;
                    clientConnection.ErrorOccurred += OnClientErrorOccurred;

                    // Will be properly registered when we receive the CLIENT_REGISTER message
                    clientConnection.StartReading();
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    OnErrorOccurred(new ErrorEventArgs($"Server error: {ex.Message}", ex));
                }
            }
        }
    }

    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        if (sender is TcpClientConnection connection)
        {
            // Register client if this is a registration message
            if (e.Message.Type == MessageType.System && e.Message.Content == "CLIENT_REGISTER")
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

    #region Enhanced TCP Client Connection
    private class TcpClientConnection(
        TcpClient tcpClient,
        string remoteAddress,
        CancellationToken cancellationToken)
        : IDisposable
    {
        private readonly NetworkStream _stream = tcpClient.GetStream();
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
                if (_disposed || !tcpClient.Connected)
                    return false;

                var json = JsonSerializer.Serialize(message);
                var data = System.Text.Encoding.UTF8.GetBytes(json + "\n");

                await _stream.WriteAsync(data, cancellationToken);
                await _stream.FlushAsync();
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
            try
            {
                using var reader = new StreamReader(_stream, System.Text.Encoding.UTF8, leaveOpen: true);

                while (!cancellationToken.IsCancellationRequested &&
                       !_disposed &&
                       tcpClient.Connected &&
                       await reader.ReadLineAsync() is { } json)
                {
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
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
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
                    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                    _stream?.Close();
                    // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
                    tcpClient?.Close();
                }
                catch { /*ignored*/ }

                try
                {
                    _stream?.Dispose();
                    tcpClient?.Dispose();
                }
                catch { /*ignored*/ }

                try
                {
                    _readTask?.Wait(1000);
                }
                catch { /*ignored*/ }
            }
        }
    }
    #endregion
}
