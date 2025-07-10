
using System.Net;
using System.Net.Sockets;
using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Rtp;

namespace Messaging.ModelLibrary.RTP;

public class RtpClient : IMessageClient
{
    #region Fields

    private UdpClient? _udpClient;
    private IPEndPoint? _remoteEndPoint;
    private Task? _receiveTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private ushort _sequenceNumber;
    private uint _ssrc;
    private bool _isDisposed;

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
    public TransportType TransportType => TransportType.Rtp;

    #endregion

    #region Methods

    /// <summary>
    /// Establishes a connection to an RTP server using the provided configuration settings.
    /// 
    /// Configuration dictionary keys:
    /// - "ServerAddress" (string, required): The RTP server IP address or hostname
    /// - "ServerPort" (int, required): The RTP server port number
    /// - "LocalPort" (int, optional): Local port to bind to. Defaults to 0 (any available port)
    /// - "ClientName" (string, optional): The identifier used for this client. Defaults to the current user's name.
    /// - "ReceiveTimeout" (int, optional): UDP receive timeout in milliseconds. Defaults to 5000.
    /// - "SendTimeout" (int, optional): UDP send timeout in milliseconds. Defaults to 5000.
    /// </summary>
    /// <param name="configuration">Dictionary containing RTP connection configuration.</param>
    /// <returns>True if the connection was successful; otherwise, false.</returns>

    public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();
            var serverAddress = configuration.GetValueOrDefault("ServerAddress") as string;
            var serverPort = configuration.GetValueOrDefault("ServerPort") as int?;

            if (string.IsNullOrEmpty(serverAddress) || !serverPort.HasValue)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs("ServerAddress and ServerPort are required for RTP connection"));
                return false;
            }

            var localPort = configuration.GetValueOrDefault("LocalPort", 0) as int? ?? 0;
            var clientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;
            var receiveTimeout = configuration.GetValueOrDefault("ReceiveTimeout", 5000) as int? ?? 5000;
            var sendTimeout = configuration.GetValueOrDefault("SendTimeout", 5000) as int? ?? 5000;

            if (!IPAddress.TryParse(serverAddress, out var serverIp))
            {
                var hostEntry = await Dns.GetHostEntryAsync(serverAddress);
                serverIp = hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                           ?? throw new ArgumentException($"Could not resolve server address: {serverAddress}");
            }

            _remoteEndPoint = new IPEndPoint(serverIp, serverPort.Value);
            _udpClient = new UdpClient(localPort);
            _udpClient.Client.ReceiveTimeout = receiveTimeout;
            _udpClient.Client.SendTimeout = sendTimeout;

            _ssrc = (uint)Random.Shared.Next();

            _sequenceNumber = 0;

            _cancellationTokenSource = new();

            ConnectionInfo = new ConnectionInfo
            {
                Id = _ssrc.ToString(),
                Name = clientName,
                Address = $"{serverAddress}:{serverPort}",
                Properties = new Dictionary<string, object>
                {
                    ["SSRC"] = _ssrc,
                    ["LocalPort"] = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port
                }
            };
            var handshakeMessage = new Message
            {
                Content = "RTP_CONNECT",
                Sender = clientName,
                Type = MessageType.Handshake
            };
            IsConnected = true;
            var success = await SendMessageAsync(handshakeMessage);
            if (!success)
            {
                await DisconnectAsync();
                return false;
            }
            _receiveTask = Task.Run(ReceiveMessagesAsync, _cancellationTokenSource.Token);
            IsConnected = true;
            Connected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP connection failed: {ex.Message}", ex));
            return false;
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            while (_cancellationTokenSource is { Token.IsCancellationRequested: false }
                   && !_isDisposed
                   && _udpClient is not null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true || _isDisposed)
                    {
                        break;
                    }

                    try
                    {
                        var rtpPacket = RtpPacket.FromBytes(result.Buffer);
                        var message = RtpMessageConverter.RtpPacketToMessage(rtpPacket);

                        if (ConnectionInfo is null) continue;
                        if (message.Type is MessageType.Handshake && message.Content.StartsWith("RTP_CONNECTED"))
                        {
                            continue;
                        }
                        MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP packet parse error: {ex.Message}", ex, ConnectionInfo));
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    // Timeout is expected, continue receiving
                }
                catch (ObjectDisposedException)
                {
                    // UDP client was disposed - expected during shutdown
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
                var disconnectMessage = new Message()
                {
                    Content = "RTP_DISCONNECT",
                    Sender = ConnectionInfo.Name,
                    Type = MessageType.Handshake
                };
                try
                {
                    await SendMessageAsync(disconnectMessage);
                }
                catch (Exception)
                {
                    /*Ignore*/
                }
            }

            _cancellationTokenSource?.Cancel();

            if (_receiveTask is not null)
            {
                try
                {
                    if (Task.WhenAny(_receiveTask, Task.Delay(TimeSpan.FromSeconds(3))) == _receiveTask)
                    {
                        await _receiveTask;
                    }
                }
                catch (Exception)
                {
                    /*Ignore*/
                }

                _receiveTask = null;
            }
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
            _remoteEndPoint = null;

            if (ConnectionInfo != null)
            {
                Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP disconnection error: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(Message message)
    {
        if (!IsConnected || _udpClient is null || _remoteEndPoint is null || _isDisposed) return false;
        try
        {
            _sequenceNumber++;
            var rtpPacket = RtpMessageConverter.MessageToRtpPacket(message, _sequenceNumber, _ssrc);
            var data = rtpPacket.ToBytes();
            await _udpClient.SendAsync(data, data.Length, _remoteEndPoint);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            try
            {
                DisconnectAsync().Wait(3000);
            }
            catch (Exception)
            {
                /*Ignore*/
            }
        }
    }

    #endregion

}