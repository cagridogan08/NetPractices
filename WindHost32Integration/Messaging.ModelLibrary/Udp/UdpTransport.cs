using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Udp;

public class UdpTransport : MessageTransportBase
{
    #region Fields

    private UdpClient? _udpServer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;
    private IPEndPoint? _localEndPoint;
    private readonly ConcurrentDictionary<string, IPEndPoint> _clientEndPoints = new();

    #endregion

    #region Properties

    public new event EventHandler<ConnectionEventArgs>? ClientDisconnected;
    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.Udp;
    #endregion

    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
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
            OnErrorOccurred(new ErrorEventArgs($"Failed to start UDP server: {ex.Message}", ex));
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
                catch {/*ignored*/  }
                _serverTask = null;
            }

            try
            {
                _udpServer?.Close();
            }
            catch {/*ignored*/ }

            _udpServer?.Dispose();
            _udpServer = null;

            // Notify all clients they are disconnected
            foreach (var connection in _connections.Values.ToList())
            {
                ClientDisconnected?.Invoke(this, new ConnectionEventArgs(connection.ConnectionInfo));
            }

            _connections.Clear();
            _clientEndPoints.Clear();
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

            if (_clientEndPoints.TryGetValue(connectionId, out var endPoint))
            {
                return await SendToEndPointAsync(message, endPoint);
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
            var tasks = _clientEndPoints.Values.Select(endPoint => SendToEndPointAsync(message, endPoint));
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
        return transportSpecificData is IPEndPoint endPoint
            ? endPoint.ToString()
            : "Unknown";
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
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Send to endpoint error: {ex.Message}", ex));
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
                        var clientKey = message?.Sender ?? clientEndPoint.ToString();

                        if (message?.Type == MessageType.System)
                        {
                            if (message.Content == "CLIENT_REGISTER")
                            {
                                _clientEndPoints[clientKey] = clientEndPoint;
                                RegisterClient(clientKey, message.Sender, clientEndPoint);
                            }
                            else if (message.Content == "CLIENT_UNREGISTER")
                            {
                                _clientEndPoints.TryRemove(clientKey, out _);
                                UnregisterClient(clientKey);
                            }
                            else
                            {
                                HandleReceivedMessage(message, clientKey);
                            }
                        }
                        else if (message != null)
                        {
                            // Update client endpoint if it changed
                            _clientEndPoints.AddOrUpdate(clientKey, clientEndPoint, (_, _) => clientEndPoint);

                            // Auto-register if not already registered
                            if (!_connections.ContainsKey(clientKey))
                            {
                                RegisterClient(clientKey, message.Sender, clientEndPoint);
                            }
                            HandleReceivedMessage(message, clientKey);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"Message parse error: {ex.Message}", ex));
            }
            catch (SocketException) { break; }
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
}