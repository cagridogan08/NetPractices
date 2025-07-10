using Grpc.Core;
using Grpc.Net.Client;
using static Messaging.ModelLibrary.Grpc.MessagingService;

namespace Messaging.ModelLibrary.Grpc;

public class GrpcClient : IMessageClient
{
    #region Fields

    private GrpcChannel? _channel;
    private MessagingServiceClient? _client;
    private AsyncDuplexStreamingCall<GrpcMessage, GrpcMessage>? _streamingCall;
    private Task? _receiveTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    #endregion

    #region Events

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Properties

    public bool IsConnected => _streamingCall != null && !_disposed;
    public ConnectionInfo? ConnectionInfo { get; private set; }
    public TransportType TransportType => TransportType.gRPC;

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
    public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var serverAddress = configuration.GetValueOrDefault("ServerAddress") as string;
            if (string.IsNullOrEmpty(serverAddress))
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs("ServerAddress is required for gRPC connection"));
                return false;
            }

            var clientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;
            var maxMessageSize = configuration.GetValueOrDefault("MaxReceiveMessageSize", 4 * 1024 * 1024) as int? ?? 4 * 1024 * 1024;

            var channelOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = maxMessageSize
            };

            if (configuration.TryGetValue("Credentials", out var credentials) && credentials is ChannelCredentials channelCredentials)
            {
                channelOptions.Credentials = channelCredentials;
            }

            var httpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            _channel = GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions
            {
                HttpHandler = httpHandler,
                MaxReceiveMessageSize = maxMessageSize,
            });
            _client = new MessagingServiceClient(_channel);
            _cancellationTokenSource = new CancellationTokenSource();

            // Start the streaming call
            _streamingCall = _client.StreamMessages(cancellationToken: _cancellationTokenSource.Token);

            // Get connection info from server
            try
            {
                var connectionResponse = await _client.GetConnectionInfoAsync(new Empty(),
                    cancellationToken: _cancellationTokenSource.Token);

                ConnectionInfo = new ConnectionInfo
                {
                    Id = connectionResponse.Id,
                    Name = clientName,
                    Address = serverAddress,
                    ConnectedAt = DateTimeOffset.FromUnixTimeSeconds(connectionResponse.ConnectedAt).DateTime,
                    IsActive = connectionResponse.IsActive,
                    Properties = connectionResponse.Properties.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
                };
            }
            catch
            {
                // Fallback if server doesn't support GetConnectionInfo
                ConnectionInfo = new ConnectionInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = clientName,
                    Address = serverAddress
                };
            }

            // Send handshake message
            var handshakeMessage = new GrpcMessage
            {
                Id = Guid.NewGuid().ToString(),
                Content = "CONNECT",
                Sender = clientName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Type = GrpcMessageType.Handshake
            };

            await _streamingCall.RequestStream.WriteAsync(handshakeMessage);

            // Start receiving messages
            _receiveTask = Task.Run(ReceiveMessagesAsync, _cancellationTokenSource.Token);

            Connected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"gRPC connection failed: {ex.Message}", ex));
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_streamingCall != null)
            {
                try
                {
                    await _streamingCall.RequestStream.CompleteAsync();
                }
                catch (Exception)
                {
                    // Ignore completion errors
                }

                _streamingCall.Dispose();
                _streamingCall = null;
            }

            if (_receiveTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_receiveTask, Task.Delay(2000)) == _receiveTask)
                    {
                        await _receiveTask;
                    }
                }
                catch (Exception)
                {
                    // Ignore task completion errors
                }
                _receiveTask = null;
            }

            _channel?.Dispose();
            _channel = null;
            _client = null;

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
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"gRPC disconnection error: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _streamingCall == null || _disposed)
                return false;

            var grpcMessage = new GrpcMessage
            {
                Id = message.Id,
                Content = message.Content,
                Sender = message.Sender,
                Receiver = message.Receiver,
                Timestamp = ((DateTimeOffset)message.Timestamp).ToUnixTimeSeconds(),
                Type = (GrpcMessageType)(int)message.Type
            };

            // Add metadata
            foreach (var kvp in message.Metadata)
            {
                grpcMessage.Metadata[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
            }

            await _streamingCall.RequestStream.WriteAsync(grpcMessage);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"gRPC send error: {ex.Message}", ex, ConnectionInfo));
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
                        MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"gRPC message parse error: {ex.Message}", ex, ConnectionInfo));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            if (!_disposed && _cancellationTokenSource?.Token.IsCancellationRequested != true)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"gRPC receive error: {ex.Message}", ex, ConnectionInfo));
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