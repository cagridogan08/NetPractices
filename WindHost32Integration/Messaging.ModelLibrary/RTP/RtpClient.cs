
using System.Net;
using System.Net.Sockets;
using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Rtp;

namespace Messaging.ModelLibrary.RTP;

public class RtpClient : MessageClientBase
{
    #region Fields
    private UdpClient? _udpClient;
    private IPEndPoint? _remoteEndPoint;
    private Task? _receiveTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private ushort _sequenceNumber;
    private uint _ssrc;
    #endregion

    #region Properties
    public override bool IsConnected => _udpClient != null && _remoteEndPoint is not null;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.Rtp;
    #endregion

    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var serverAddress = configuration.GetValueOrDefault("ServerAddress") as string;
            var serverPort = configuration.GetValueOrDefault("ServerPort") as int?;

            if (string.IsNullOrEmpty(serverAddress) || !serverPort.HasValue)
            {
                OnErrorOccurred(new ErrorEventArgs("ServerAddress and ServerPort are required for RTP connection"));
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
                Id = clientName,
                Name = clientName,
                Address = $"{serverAddress}:{serverPort}",
                ConnectedAt = DateTime.UtcNow,
                IsActive = true,
                Properties = new Dictionary<string, object>
                {
                    ["SSRC"] = _ssrc,
                    ["LocalPort"] = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port
                }
            };

            // Send registration message
            var registrationMessage = new Message
            {
                Content = "CLIENT_REGISTER",
                Sender = clientName,
                Receiver = "System",
                Type = MessageType.System
            };

            var success = await SendMessageAsync(registrationMessage);
            if (!success)
            {
                await DisconnectAsync();
                return false;
            }

            _receiveTask = Task.Run(ReceiveMessagesAsync, _cancellationTokenSource.Token);
            OnConnected(new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"RTP connection failed: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected && ConnectionInfo is not null)
            {
                var disconnectMessage = new Message()
                {
                    Content = "CLIENT_UNREGISTER",
                    Sender = ConnectionInfo.Name,
                    Receiver = "System",
                    Type = MessageType.System
                };
                try
                {
                    await SendMessageAsync(disconnectMessage);
                }
                catch
                {
                    /*ignored*/
                }
            }

            _cancellationTokenSource?.Cancel();

            if (_receiveTask is not null)
            {
                try
                {
                    if (await Task.WhenAny(_receiveTask, Task.Delay(TimeSpan.FromSeconds(3))) == _receiveTask)
                        await _receiveTask;
                }
                catch
                {
                    /*ignored*/
                }
                _receiveTask = null;
            }

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
            _remoteEndPoint = null;

            if (ConnectionInfo != null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"RTP disconnection error: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        if (!IsConnected || _udpClient is null || _remoteEndPoint is null || _disposed)
            return false;

        try
        {
            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            _sequenceNumber++;
            var rtpPacket = RtpMessageConverter.MessageToRtpPacket(message, _sequenceNumber, _ssrc);
            var data = rtpPacket.ToBytes();
            await _udpClient.SendAsync(data, data.Length, _remoteEndPoint);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"RTP send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            while (_cancellationTokenSource is { Token.IsCancellationRequested: false }
                   && !_disposed
                   && _udpClient is not null)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true || _disposed)
                        break;

                    try
                    {
                        var rtpPacket = RtpPacket.FromBytes(result.Buffer);
                        var message = RtpMessageConverter.RtpPacketToMessage(rtpPacket);

                        if (ConnectionInfo is null) continue;
                        if (message.Type is MessageType.Handshake && message.Content.StartsWith("RTP_CONNECTED"))
                            continue;

                        OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(new ErrorEventArgs($"RTP packet parse error: {ex.Message}", ex, ConnectionInfo));
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut) { }
                catch (ObjectDisposedException) { break; }
            }
        }
        catch (Exception ex)
        {
            if (!_disposed && _cancellationTokenSource?.Token.IsCancellationRequested != true)
                OnErrorOccurred(new ErrorEventArgs($"RTP receive error: {ex.Message}", ex, ConnectionInfo));
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            base.Dispose();
            try
            {
                DisconnectAsync().Wait(3000);
            }
            catch
            {
                /*ignored*/
            }
        }
    }
}
