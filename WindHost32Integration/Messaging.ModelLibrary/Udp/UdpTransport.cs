using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Udp;

public class UdpTransport : IMessageTransport
{
    #region Fields

    private readonly ConcurrentDictionary<string, UdpClientInfo> _connections = new();
    private UdpClient? _udpServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private bool _disposed;
    private IPEndPoint? _localEndPoint;

    #endregion

    #region Events

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? ClientConnected;
    public event EventHandler<ConnectionEventArgs>? ClientDisconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Properties

    public bool IsRunning { get; private set; }
    public TransportType TransportType => TransportType.Udp;
    public IReadOnlyList<ConnectionInfo> Connections => _connections.Values.Select(c => c.ConnectionInfo).ToList();

    #endregion

    #region Methods


    /// <summary>
    /// Starts a UDP server using the specified configuration settings.
    /// If an existing server is running, it is stopped before starting a new one.
    /// 
    /// Optional configuration dictionary keys:
    /// - "Host" (string, optional): The IP address or hostname to bind to. 
    ///   Defaults to "localhost" (binds to IPAddress.Any).
    /// - "Port" (int, optional): The port number to bind the UDP server to. Defaults to 8080.
    /// 
    /// Initializes a UdpClient bound to the given endpoint and starts listening for incoming datagrams asynchronously.
    /// Raises the ErrorOccurred event if the server fails to start.
    /// </summary>
    /// <param name="configuration">Optional dictionary containing UDP server configuration settings.</param>
    /// <returns>True if the server started successfully; otherwise, false.</returns>
    public async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            var host = configuration?.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration?.GetValueOrDefault("Port", 8080) as int? ?? 8080;

            var ipAddress = host == "localhost" ? IPAddress.Any : IPAddress.Parse(host);
            _localEndPoint = new IPEndPoint(ipAddress, port);
            _udpServer = new UdpClient(_localEndPoint);

            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(RunServerAsync, _cancellationTokenSource.Token);

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start UDP server: {ex.Message}", ex));
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
                _udpServer?.Close();
            }
            catch (Exception)
            {
                // Ignore close errors
            }

            _udpServer?.Dispose();
            _udpServer = null;

            // Notify all clients they are disconnected
            foreach (var connection in _connections.Values.ToList())
            {
                ClientDisconnected?.Invoke(this, new ConnectionEventArgs(connection.ConnectionInfo));
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

            if (_connections.TryGetValue(connectionId, out var clientInfo))
            {
                return await SendToEndPointAsync(message, clientInfo.EndPoint);
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
            var tasks = _connections.Values.Select(clientInfo => SendToEndPointAsync(message, clientInfo.EndPoint));
            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to broadcast message: {ex.Message}", ex));
            return false;
        }
    }

    private async Task<bool> SendToEndPointAsync(Message message, IPEndPoint endPoint)
    {
        try
        {
            if (_disposed || _udpServer == null) return false;

            var json = JsonSerializer.Serialize(message);
            var data = System.Text.Encoding.UTF8.GetBytes(json);

            await _udpServer.SendAsync(data, data.Length, endPoint);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send to endpoint error: {ex.Message}", ex));
            return false;
        }
    }

    private async Task RunServerAsync()
    {
        while (_cancellationTokenSource is { Token.IsCancellationRequested: false })
        {
            try
            {
                if (_udpServer != null)
                {
                    var result = await _udpServer.ReceiveAsync();
                    var json = System.Text.Encoding.UTF8.GetString(result.Buffer);

                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var message = JsonSerializer.Deserialize<Message>(json);
                        var clientEndPoint = result.RemoteEndPoint;
                        var clientKey = clientEndPoint.ToString();

                        // Handle connection/disconnection messages
                        if (message?.Content == "CONNECT")
                        {
                            if (!_connections.TryGetValue(clientKey, out var connection))
                            {
                                var connectionInfo = new ConnectionInfo
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Name = message.Sender,
                                    Address = clientEndPoint.ToString()
                                };

                                var clientInfo = new UdpClientInfo(connectionInfo, clientEndPoint)
                                {
                                    LastSeen = DateTime.Now
                                };

                                _connections.TryAdd(clientKey, clientInfo);
                                ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
                            }
                            else
                            {
                                // Update last seen time
                                connection.LastSeen = DateTime.Now;
                            }
                        }
                        else if (message?.Content == "DISCONNECT")
                        {
                            if (_connections.TryRemove(clientKey, out var clientInfo))
                            {
                                ClientDisconnected?.Invoke(this, new ConnectionEventArgs(clientInfo.ConnectionInfo));
                            }
                        }
                        else
                        {
                            // Regular message
                            if (_connections.TryGetValue(clientKey, out var clientInfo))
                            {
                                clientInfo.LastSeen = DateTime.Now;
                                if (message != null)
                                    MessageReceived?.Invoke(this,
                                        new MessageEventArgs(message, clientInfo.ConnectionInfo));
                            }
                            else
                            {
                                message ??= new Message
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Sender = "Unknown",
                                    Content = "Unregistered client message"
                                };
                                // Message from unknown client - auto-register them
                                var connectionInfo = new ConnectionInfo
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Name = message.Sender,
                                    Address = clientEndPoint.ToString()
                                };

                                var newClientInfo = new UdpClientInfo(connectionInfo, clientEndPoint)
                                {

                                    LastSeen = DateTime.Now
                                };

                                _connections.TryAdd(clientKey, newClientInfo);
                                ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
                                MessageReceived?.Invoke(this, new MessageEventArgs(message, connectionInfo));
                            }
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex));
            }
            catch (SocketException)
            {
                // Socket was closed - this is expected during shutdown
                break;
            }
            catch (ObjectDisposedException)
            {
                // UDP client was disposed - this is expected during shutdown
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

    private record UdpClientInfo(ConnectionInfo ConnectionInfo, IPEndPoint EndPoint)
    {
        public DateTime LastSeen { get; set; }
    }
}