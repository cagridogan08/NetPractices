using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Messaging.ModelLibrary.Abstract;
using static Messaging.ModelLibrary.Grpc.MessagingService;

namespace Messaging.ModelLibrary.Grpc;

public class GrpcClient : MessageClientBase
{
    #region Fields

    private GrpcChannel? _channel;
    private MessagingServiceClient? _client;
    private AsyncDuplexStreamingCall<GrpcMessage, GrpcMessage>? _streamingCall;
    private Task? _receiveTask;
    private CancellationTokenSource? _cancellationTokenSource;

    #endregion



    #region Properties

    public override bool IsConnected => _streamingCall != null && !_disposed;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.gRPC;

    #endregion

    #region Methods

    /// <summary>
    /// Establishes a connection to a gRPC server using the provided configuration settings.
    /// 
    /// Configuration dictionary keys:
    /// - "ServerAddress" (string, required): The gRPC server address (e.g., "https://localhost:5001")
    /// - "ClientName" (string, optional): The identifier used for this client. Defaults to the current user's name.
    /// - "MaxReceiveMessageSize" (int, optional): Maximum message size in bytes. Defaults to 4MB.
    /// - "Credentials" (ChannelCredentials, optional): gRPC channel credentials. Defaults to insecure.
    /// </summary>
    /// <param name="configuration">Dictionary containing gRPC connection configuration.</param>
    /// <returns>True if the connection was successful; otherwise, false.</returns>
    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var serverAddress = configuration.GetValueOrDefault("ServerAddress") as string;
            if (string.IsNullOrEmpty(serverAddress))
            {
                OnErrorOccurred(new ErrorEventArgs("ServerAddress is required for gRPC connection"));
                return false;
            }

            var clientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;
            var maxMessageSize = configuration.GetValueOrDefault("MaxReceiveMessageSize", 4 * 1024 * 1024) as int? ?? 4 * 1024 * 1024;

            var httpHandler = new HttpClientHandler();

            if (serverAddress.StartsWith("http://") ||
                configuration.GetValueOrDefault("DisableCertificateValidation", false) as bool? == true)
            {
                httpHandler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            var channelOptions = new GrpcChannelOptions
            {
                HttpHandler = httpHandler,
                MaxReceiveMessageSize = maxMessageSize,
                MaxSendMessageSize = maxMessageSize,
                ServiceConfig = new ServiceConfig
                {
                    MethodConfigs =
                    {
                        new MethodConfig
                        {
                            Names = { MethodName.Default },
                            RetryPolicy = new RetryPolicy
                            {
                                MaxAttempts = 3,
                                InitialBackoff = TimeSpan.FromSeconds(1),
                                MaxBackoff = TimeSpan.FromSeconds(5),
                                BackoffMultiplier = 1.5,
                                RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.DeadlineExceeded }
                            }
                        }
                    }
                }
            };

            if (configuration.TryGetValue("Credentials", out var credentials) && credentials is ChannelCredentials channelCredentials)
            {
                channelOptions.Credentials = channelCredentials;
            }

            _channel = GrpcChannel.ForAddress(serverAddress, channelOptions);
            _client = new MessagingServiceClient(_channel);
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var connectionResponse = await _client.GetConnectionInfoAsync(new Empty(),
                    deadline: DateTime.UtcNow.AddSeconds(10),
                    cancellationToken: _cancellationTokenSource.Token);

                ConnectionInfo = new ConnectionInfo
                {
                    Id = clientName, // Use client name for easier routing
                    Name = clientName,
                    Address = serverAddress,
                    ConnectedAt = DateTimeOffset.FromUnixTimeSeconds(connectionResponse.ConnectedAt).DateTime,
                    IsActive = connectionResponse.IsOnline,
                    Properties = connectionResponse.Properties.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
                };
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
            {
                ConnectionInfo = new ConnectionInfo
                {
                    Id = clientName,
                    Name = clientName,
                    Address = serverAddress,
                    ConnectedAt = DateTime.UtcNow,
                    IsActive = true
                };
            }

            _streamingCall = _client.StreamMessages(cancellationToken: _cancellationTokenSource.Token);

            var handshakeMessage = new GrpcMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = "CLIENT_REGISTER",
                Sender = clientName,
                Receiver = "System",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Type = GrpcMessageType.Handshake
            };

            await _streamingCall.RequestStream.WriteAsync(handshakeMessage);

            _receiveTask = Task.Run(ReceiveMessagesAsync, _cancellationTokenSource.Token);

            OnConnected(new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"gRPC connection failed: {ex.Message}", ex));

            try
            {
                await DisconnectAsync();
            }
            catch
            {
                // Ignore errors during disconnection
            }

            return false;
        }
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            // Send unregister message
            if (_streamingCall != null && ConnectionInfo != null)
            {
                var unregisterMessage = new GrpcMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = "CLIENT_UNREGISTER",
                    Sender = ConnectionInfo.Name,
                    Receiver = "System",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Type = GrpcMessageType.Handshake
                };

                try
                {
                    await _streamingCall.RequestStream.WriteAsync(unregisterMessage);
                }
                catch
                {
                    /*ignore*/
                }
            }

            _cancellationTokenSource?.Cancel();

            if (_streamingCall != null)
            {
                try
                {
                    await _streamingCall.RequestStream.CompleteAsync();
                }
                catch
                {
                    /*ignore*/
                }

                _streamingCall.Dispose();
                _streamingCall = null;
            }

            if (_receiveTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_receiveTask, Task.Delay(2000)) == _receiveTask)
                        await _receiveTask;
                }
                catch
                {
                    //* Ignore any exceptions during receive task completion
                }
                _receiveTask = null;
            }

            _channel?.Dispose();
            _channel = null;
            _client = null;

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
            OnErrorOccurred(new ErrorEventArgs($"gRPC disconnection error: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _streamingCall == null || _disposed)
                return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var grpcMessage = new GrpcMessage
            {
                Id = message.Id,
                Content = message.Content,
                Sender = message.Sender,
                Receiver = message.Receiver,
                Timestamp = ((DateTimeOffset)message.Timestamp).ToUnixTimeSeconds(),
                Type = (GrpcMessageType)(int)message.Type
            };

            foreach (var kvp in message.Metadata)
            {
                grpcMessage.Metadata[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
            }

            await _streamingCall.RequestStream.WriteAsync(grpcMessage);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"gRPC send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            if (_streamingCall == null) return;

            await foreach (var grpcMessage in _streamingCall.ResponseStream.ReadAllAsync(_cancellationTokenSource?.Token ?? CancellationToken.None))
            {
                if (_disposed || _cancellationTokenSource?.Token.IsCancellationRequested == true)
                    break;

                try
                {
                    var message = new Message
                    {
                        Id = grpcMessage.Id,
                        Content = grpcMessage.Content,
                        Sender = grpcMessage.Sender,
                        Receiver = grpcMessage.Receiver,
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(grpcMessage.Timestamp).DateTime,
                        Type = (MessageType)(int)grpcMessage.Type,
                        Metadata = grpcMessage.Metadata.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
                    };

                    if (ConnectionInfo != null)
                    {
                        OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
                    }
                }
                catch (Exception ex)
                {
                    OnErrorOccurred(new ErrorEventArgs($"gRPC message parse error: {ex.Message}", ex, ConnectionInfo));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
        catch (Exception ex)
        {
            if (!_disposed && _cancellationTokenSource?.Token.IsCancellationRequested != true)
            {
                OnErrorOccurred(new ErrorEventArgs($"gRPC receive error: {ex.Message}", ex, ConnectionInfo));
            }
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            try
            {
                DisconnectAsync().Wait(3000);
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }
    }

    #endregion
}