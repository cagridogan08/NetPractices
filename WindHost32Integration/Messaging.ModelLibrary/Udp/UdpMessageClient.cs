using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Messaging.ModelLibrary.Udp;

public class UdpMessageClient : IMessageClient
{

    #region Fields

    private UdpClient? _udpClient;

    private Task? _readTask;

    private CancellationTokenSource? _cancellationTokenSource;

    private bool _disposed;

    private IPEndPoint? _serverEndpoint;


    #endregion

    #region Events

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Properties


    public bool IsConnected { get; private set; }
    public ConnectionInfo? ConnectionInfo { get; private set; }
    public TransportType TransportType => TransportType.Udp;

    #endregion

    #region Methods

    public void Dispose()
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


    public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
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

            IsConnected = true;
            Connected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));

            // Send a connection message to announce ourselves
            await SendMessageAsync(new Message
            {
                Content = "CONNECT",
                Sender = clientName,
                Type = MessageType.Text
            });

            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Connection failed: {ex.Message}", ex));
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
                        MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
                }
                catch (JsonException ex)
                {
                    if (ConnectionInfo != null)
                        ErrorOccurred?.Invoke(this,
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

    public async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected && ConnectionInfo is not null)
            {
                await SendMessageAsync(new Message()
                {
                    Content = "DISCONNECT",
                    Sender = ConnectionInfo.Name,
                    Type = MessageType.Text
                });
            }
            IsConnected = false;
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
                catch (Exception e)
                {
                    /*Ignore*/
                }
                _udpClient?.Dispose();
                _udpClient = null;

                if (ConnectionInfo is not null)
                {
                    Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                    ConnectionInfo = null;
                }
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
            if (!IsConnected || _disposed || _udpClient is null) return false;

            var json = JsonSerializer.Serialize(message);
            var data = System.Text.Encoding.UTF8.GetBytes(json);

            if (_serverEndpoint is null)
            {
                throw new InvalidOperationException("Server endpoint is not set.");
            }
            await _udpClient.SendAsync(data, data.Length, _serverEndpoint);
            return true;

        }
        catch (Exception ex)
        {
            if (ConnectionInfo != null)
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    #endregion

}