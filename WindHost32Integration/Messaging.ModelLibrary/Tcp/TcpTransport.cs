using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Messaging.ModelLibrary.Tcp;

public class TcpTransport : IMessageTransport
{
    #region Fields

    private readonly ConcurrentDictionary<string, TcpClientConnection> _connections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private bool _disposed;

    #endregion

    #region Events


    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? ClientConnected;
    public event EventHandler<ConnectionEventArgs>? ClientDisconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Properties

    public bool IsRunning { get; private set; }
    public TransportType TransportType => TransportType.Tcp;
    public IReadOnlyList<ConnectionInfo> Connections => _connections.Values.Select(c => c.Info).ToList();

    #endregion

    #region Methods

    /// <summary>
    /// Starts a TCP server using the specified configuration.
    /// Stops any existing listener before starting a new one.
    /// 
    /// Optional configuration dictionary keys:
    /// - "Host" (string, optional): The IP address or hostname to bind the server to. 
    ///   Defaults to "localhost" (binds to loopback address).
    /// - "Port" (int, optional): The port number to listen on. Defaults to 8080.
    /// 
    /// Initializes the TCP listener and begins accepting incoming client connections asynchronously.
    /// Raises the ErrorOccurred event if startup fails.
    /// </summary>
    /// <param name="configuration">Optional dictionary of server configuration settings.</param>
    /// <returns>True if the server started successfully; otherwise, false.</returns>
    public async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
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
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start TCP server: {ex.Message}", ex));
            return false;
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
                _listener?.Stop();
            }
            catch (Exception)
            {
                // Ignore listener stop errors
            }

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

    public async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
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

    private async Task RunServerAsync()
    {
        while (_cancellationTokenSource is { Token.IsCancellationRequested: false })
        {
            try
            {
                if (_listener != null)
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync();

                    var connectionInfo = new ConnectionInfo
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = "Unknown",
                        Address = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown"
                    };

                    var clientConnection = new TcpClientConnection(tcpClient, connectionInfo, _cancellationTokenSource.Token);
                    clientConnection.MessageReceived += OnClientMessageReceived;
                    clientConnection.Disconnected += OnClientDisconnected;
                    clientConnection.ErrorOccurred += OnClientErrorOccurred;

                    _connections.TryAdd(connectionInfo.Id, clientConnection);
                    ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));

                    clientConnection.StartReading();
                }
            }
            catch (ObjectDisposedException)
            {
                // Listener was disposed - this is expected during shutdown
                break;
            }
            catch (OperationCanceledException)
            {
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

    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        // Update connection name if this is the first message
        if (sender is TcpClientConnection connection && connection.Info.Name == "Unknown")
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
    #endregion

    #region TcpClientConnection
    private class TcpClientConnection(TcpClient tcpClient, ConnectionInfo info, CancellationToken cancellationToken)
        : IDisposable
    {
        #region Fields

        private readonly NetworkStream _stream = tcpClient.GetStream();
        private Task? _readTask;
        private bool _disposed;

        #endregion

        #region Events

        public event EventHandler<MessageEventArgs>? MessageReceived;
        public event EventHandler<ConnectionEventArgs>? Disconnected;
        public event EventHandler<ErrorEventArgs>? ErrorOccurred;

        #endregion

        #region Properties

        public ConnectionInfo Info { get; } = info;


        #endregion

        #region Methods

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
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, Info));
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
                            if (message != null) MessageReceived?.Invoke(this, new MessageEventArgs(message, Info));
                        }
                    }
                    catch (JsonException ex)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex, Info));
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
                if (!cancellationToken.IsCancellationRequested && !_disposed)
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
                    _stream?.Close();
                    tcpClient?.Close();
                }
                catch (Exception)
                {
                    // Ignore disposal errors
                }

                try
                {
                    _stream?.Dispose();
                    tcpClient?.Dispose();
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

    #endregion


    #endregion
}