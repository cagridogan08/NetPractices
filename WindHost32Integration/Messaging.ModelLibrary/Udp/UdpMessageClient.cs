using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Udp;

public sealed class UdpMessageClient : MessageClientBase
{

    #region Fields

    private UdpClient? _udpClient;

    private Task? _readTask;

    private CancellationTokenSource? _cancellationTokenSource;


    private IPEndPoint? _serverEndpoint;


    #endregion

    #region Properties


    public override bool IsConnected => _udpClient?.Client.Connected ?? false;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.Udp;

    #endregion

    #region Methods

    /// <summary>
    /// Establishes a UDP "connection" (logical setup) to a remote endpoint using the specified configuration settings.
    /// Binds a local UDP client to the specified local port and prepares to send/receive messages.
    /// 
    /// Required/optional configuration dictionary keys:
    /// - "Host" (string, optional): The remote host to connect to. Defaults to "localhost".
    /// - "Port" (int, optional): The remote port to send messages to. Defaults to 11000.
    /// - "LocalPort" (int, optional): The local UDP port to bind to. Defaults to 0 (random available port).
    /// - "ClientName" (string, optional): Name used to identify this client. Defaults to the current user's name.
    /// 
    /// Starts listening for incoming UDP messages asynchronously.
    /// Triggers the Connected event on success, and sends an initial "CONNECT" message to the remote server.
    /// Triggers the ErrorOccurred event if the connection setup fails.
    /// </summary>
    /// <param name="configuration">Dictionary containing UDP client connection configuration settings.</param>
    /// <returns>True if the client was set up successfully; otherwise, false.</returns>
    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var host = configuration.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
            var port = configuration.GetValueOrDefault("Port", 11000) as int? ?? 11000;
            var localPort = configuration.GetValueOrDefault("LocalPort", 0) as int? ?? 0;
            var clientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;

            _serverEndpoint = new IPEndPoint(IPAddress.Parse(host == "localhost" ? "127.0.0.1" : host), port);
            _udpClient = new UdpClient(localPort);

            ConnectionInfo = new ConnectionInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = clientName,
                Address = $"{host}:{port}"
            };

            _cancellationTokenSource = new CancellationTokenSource();
            _readTask = Task.Run(ReadMessagesAsync, _cancellationTokenSource.Token);

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

    private async Task? ReadMessagesAsync()
    {
        try
        {
            while (_cancellationTokenSource is { Token.IsCancellationRequested: false } &&
                   !_disposed &&
                   IsConnected &&
                   _udpClient != null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    var json = System.Text.Encoding.UTF8.GetString(result.Buffer);

                    if (string.IsNullOrWhiteSpace(json)) continue;
                    var message = JsonSerializer.Deserialize<Message>(json);
                    if (message != null && ConnectionInfo is not null)
                        OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
                }
                catch (JsonException ex)
                {
                    if (ConnectionInfo != null)
                        OnErrorOccurred(
                            new ErrorEventArgs($"Message parse error: {ex.Message}", ex, ConnectionInfo));
                }
                catch (SocketException)
                {
                    // Socket was closed - this is expected during shutdown
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected && ConnectionInfo is not null)
            {
                var unregisterMessage = new Message()
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
                    {
                        await _readTask;
                    }
                }
                catch (Exception)
                {
                    /*Ignore*/
                }

                try
                {
                    _udpClient?.Dispose();
                }
                catch (Exception)
                {
                    /*Ignore*/
                }
                _udpClient?.Dispose();
                _udpClient = null;

                if (ConnectionInfo is not null)
                {
                    OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                    ConnectionInfo = null;
                }
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
            if (!IsConnected || _disposed || _udpClient is null) return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var json = JsonSerializer.Serialize(message);
            var data = System.Text.Encoding.UTF8.GetBytes(json);

            if (_serverEndpoint is null)
                throw new InvalidOperationException("Server endpoint is not set.");

            await _udpClient.SendAsync(data, data.Length, _serverEndpoint);
            return true;

        }
        catch (Exception ex)
        {
            if (ConnectionInfo != null)
                OnErrorOccurred(new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                DisconnectAsync().Wait(2000);
            }
            catch (Exception)
            {
                /*Ignore dispose errors */
            }
        }
    }



    #endregion

}