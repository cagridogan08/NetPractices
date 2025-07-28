using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Rtp;
using Microsoft.AspNetCore.Http;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Messaging.ModelLibrary.RTP
{
    public class RtpTransport(int port = 5004, IPAddress? bindAddress = null) : IMessageTransport
    {
        #region Fields

        private readonly ConcurrentDictionary<uint, RtpClientSession> _clients = new();

        private UdpClient? _udpServer;
        private bool _isDisposed;

        private Task? _receiveTask;
        private CancellationTokenSource? _cancellationTokenSource;

        private ushort _sequenceNumber;
        private uint _serverSsrc;

        private readonly int _port = port;

        private readonly IPAddress _ipAddress = bindAddress ?? IPAddress.Any;

        #endregion

        #region Events

        public event EventHandler<MessageEventArgs>? MessageReceived;
        public event EventHandler<ConnectionEventArgs>? ClientConnected;
        public event EventHandler<ConnectionEventArgs>? ClientDisconnected;
        public event EventHandler<ErrorEventArgs>? ErrorOccurred;

        #endregion

        #region Properties

        public bool IsRunning { get; private set; }
        public TransportType TransportType => TransportType.Rtp;
        public IReadOnlyList<ConnectionInfo> Connections => _clients.Values.Select(c => c.ConnectionInfo).ToList();

        #endregion

        #region Methods

        /// <summary>
        /// Starts the RTP server with the provided configuration.
        /// 
        /// Configuration dictionary keys:
        /// - "Port" (int, optional): Server port. Defaults to constructor value.
        /// - "BindAddress" (IPAddress, optional): Address to bind to. Defaults to constructor value.
        /// - "ReceiveTimeout" (int, optional): UDP receive timeout in milliseconds. Defaults to 1000.
        /// - "SessionTimeout" (int, optional): Client session timeout in seconds. Defaults to 30.
        /// - "EnableMulticast" (bool, optional): Whether to enable multicast support. Defaults to false.
        /// - "MulticastAddress" (string, optional): Multicast group address if multicast is enabled.
        /// </summary>
        public async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
        {
            try
            {
                await StopAsync(); // Ensure any previous instance is stopped

                var port = configuration?.GetValueOrDefault("Port", _port) as int? ?? _port;
                var bindAddress = configuration?.GetValueOrDefault("BindAddress", _ipAddress) as IPAddress ?? _ipAddress;
                var receiveTimeout = configuration?.GetValueOrDefault("ReceiveTimeout", 1000) as int? ?? 1000;
                var sessionTimeout = configuration?.GetValueOrDefault("SessionTimeout", 30) as int? ?? 30;
                var enableMulticast = configuration?.GetValueOrDefault("EnableMulticast", false) as bool? ?? false;
                var multicastAddress = configuration?.GetValueOrDefault("MulticastAddress") as string;


                _udpServer = new UdpClient(new IPEndPoint(bindAddress, port));

                _udpServer.Client.ReceiveTimeout = receiveTimeout;


                if (enableMulticast && !string.IsNullOrEmpty(multicastAddress))
                {
                    if (IPAddress.TryParse(multicastAddress, out var multicaltIp))
                    {
                        _udpServer.JoinMulticastGroup(multicaltIp);
                    }
                }

                _serverSsrc = (uint)Random.Shared.Next();
                _sequenceNumber = 0;
                _cancellationTokenSource = new CancellationTokenSource();

                _ = Task.Run(() => CleanupExpiredSession(sessionTimeout), _cancellationTokenSource.Token);

                _receiveTask = Task.Run(ReceiveMessageAsync, _cancellationTokenSource.Token);

                IsRunning = true;
                return true;
            }
            catch (Exception e)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs("Failed to stop previous instance.", e));
                return false;
            }
        }

        private async Task ReceiveMessageAsync()
        {
            try
            {
                while (_cancellationTokenSource is { Token.IsCancellationRequested: false }
                       && !_isDisposed
                       && _udpServer is not null)
                {
                    try
                    {
                        var result = await _udpServer.ReceiveAsync();
                        if (_cancellationTokenSource is { Token.IsCancellationRequested: true } || _isDisposed)
                        {
                            break;
                        }

                        await ProcessMessageAsync(result.Buffer, result.RemoteEndPoint);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // Timeout is expected, continue receiving
                    }
                    catch (ObjectDisposedException)
                    {
                        // UDP server was disposed - expected during shutdown
                        break;
                    }
                    catch (Exception ex)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP receive error: {ex.Message}", ex));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                if (!_isDisposed && _cancellationTokenSource?.Token.IsCancellationRequested != true)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP receive loop error: {ex.Message}", ex));
                }
            }
        }

        private async Task ProcessMessageAsync(byte[] resultBuffer, IPEndPoint resultRemoteEndPoint)
        {
            try
            {
                var rtpPackage = RtpPacket.FromBytes(resultBuffer);
                var message = RtpMessageConverter.RtpPacketToMessage(rtpPackage);

                var session = _clients.GetOrAdd(rtpPackage.SSRC,
                    _ => CreateNewSession(rtpPackage.SSRC, resultRemoteEndPoint, message.Sender));

                session.EndPoint = resultRemoteEndPoint;
                session.UpdateLastActivity();

                if (message.Type is MessageType.Handshake)
                {
                    await HandleHandshakeMessage(session, message);
                    return;
                }

                if (!string.IsNullOrEmpty(message.Sender) && session.ConnectionInfo.Name != message.Sender)
                {
                    session.ConnectionInfo.Name = message.Sender;
                }
                MessageReceived?.Invoke(this, new MessageEventArgs(message, session.ConnectionInfo));
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP packet processing error: {ex.Message}", ex));
            }
        }

        private async Task HandleHandshakeMessage(RtpClientSession session, Message message)
        {
            try
            {
                if (message.Content == "RTP_CONNECT")
                {
                    // Send connection acknowledgment
                    var ackMessage = new Message
                    {
                        Content = $"RTP_CONNECTED:{_serverSsrc}",
                        Sender = "Server",
                        Receiver = session.ConnectionInfo.Name,
                        Type = MessageType.Handshake
                    };

                    await SendMessageToSessionAsync(session, ackMessage);
                }
                else if (message.Content == "RTP_DISCONNECT")
                {
                    // Remove session and notify disconnection
                    if (_clients.TryRemove(session.Ssrc, out _))
                    {
                        ClientDisconnected?.Invoke(this, new ConnectionEventArgs(session.ConnectionInfo));
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Handshake error: {ex.Message}", ex, session.ConnectionInfo));
            }
        }

        private RtpClientSession CreateNewSession(uint ssrc, IPEndPoint endPoint, string clientName)
        {
            var connectionInfo = new ConnectionInfo
            {
                Id = ssrc.ToString(),
                Name = clientName,
                Address = endPoint.ToString(),
                Properties = new Dictionary<string, object>
                {
                    ["SSRC"] = ssrc,
                    ["RemoteEndPoint"] = endPoint.ToString()
                }
            };

            var session = new RtpClientSession(ssrc, endPoint, connectionInfo);

            // Raise connected event
            ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));

            return session;
        }

        private async Task? CleanupExpiredSession(int sessionTimeout)
        {
            try
            {
                while (_cancellationTokenSource?.Token.IsCancellationRequested == false && !_isDisposed)
                {
                    var expiredSessions = _clients.Values
                        .Where(s => DateTime.UtcNow - s.LastActivity > TimeSpan.FromSeconds(sessionTimeout))
                        .ToList();

                    foreach (var session in expiredSessions.Where(session => _clients.TryRemove(session.Ssrc, out _)))
                    {
                        ClientDisconnected?.Invoke(this, new ConnectionEventArgs(session.ConnectionInfo));
                    }

                    await Task.Delay(TimeSpan.FromSeconds(sessionTimeout / 2.0), _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Session cleanup error: {ex.Message}", ex));
            }
        }

        public async Task StopAsync()
        {
            try
            {
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
                        /*ignore*/
                    }

                    _receiveTask = null;
                }

                var disconnectTasks = _clients.Values.Select(session => Task.Run(async () =>
                {
                    try
                    {
                        var disconnectMessage = new Message()
                        {
                            Content = "RTP_SERVER_SHUTDOWN",
                            Type = MessageType.Handshake,
                            Sender = "Server"
                        };
                        await SendMessageToSessionAsync(session, disconnectMessage);
                    }
                    catch (Exception)
                    {
                        /*Ignore*/
                    }
                }));

                try
                {
                    await Task.WhenAll(disconnectTasks);
                }
                catch (Exception)
                {
                    /*Ignore*/
                }

                foreach (var session in _clients.Values)
                {
                    ClientDisconnected?.Invoke(this, new ConnectionEventArgs(session.ConnectionInfo));
                }
                _clients.Clear();

                _udpServer?.Close();
                _udpServer?.Dispose();

                _udpServer = null;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Error stopping RTP server: {ex.Message}", ex));
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

                if (uint.TryParse(connectionId, out var ssrc) && _clients.TryGetValue(ssrc, out var session))
                {
                    return await SendMessageToSessionAsync(session, message);
                }

                return false;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send RTP message: {ex.Message}", ex));
                return false;
            }
        }

        private async Task<bool> SendMessageToSessionAsync(RtpClientSession session, Message message)
        {
            try
            {
                if (_udpServer == null || _isDisposed)
                    return false;

                _sequenceNumber++;

                var rtpPacket = RtpMessageConverter.MessageToRtpPacket(message, _sequenceNumber, _serverSsrc);
                var packetData = rtpPacket.ToBytes();

                await _udpServer.SendAsync(packetData, session.EndPoint);
                session.UpdateLastActivity();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP send error to {session.EndPoint}: {ex.Message}", ex, session.ConnectionInfo));
                return false;
            }
        }

        public async Task<bool> BroadcastMessageAsync(Message message)
        {
            try
            {
                var tasks = _clients.Values.Select(session => SendMessageToSession(session, message));
                var results = await Task.WhenAll(tasks);
                return results.Any(r => r);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to broadcast RTP message: {ex.Message}", ex));
                return false;
            }
        }
        private async Task<bool> SendMessageToSession(RtpClientSession session, Message message)
        {
            try
            {
                if (_udpServer == null || _isDisposed)
                    return false;

                _sequenceNumber++;

                var rtpPacket = RtpMessageConverter.MessageToRtpPacket(message, _sequenceNumber, _serverSsrc);
                var packetData = rtpPacket.ToBytes();

                await _udpServer.SendAsync(packetData, session.EndPoint);
                session.UpdateLastActivity();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"RTP send error to {session.EndPoint}: {ex.Message}", ex, session.ConnectionInfo));
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
                    StopAsync().Wait(5000);
                }
                catch (Exception)
                {
                    // Ignore disposal errors
                }
            }
        }

        #endregion

        #region ClientHandler

        private class RtpClientSession(uint ssrc, IPEndPoint endPoint, ConnectionInfo connectionInfo)
        {
            public uint Ssrc { get; } = ssrc;
            public IPEndPoint EndPoint { get; set; } = endPoint;
            public ConnectionInfo ConnectionInfo { get; } = connectionInfo;
            public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

            public void UpdateLastActivity()
            {
                LastActivity = DateTime.UtcNow;
            }
        }

        #endregion
    }
}
