using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Grpc;

public class GrpcTransport(string address = "localhost", int port = 5000) : IMessageTransport
{
    #region Fields

    private readonly ConcurrentDictionary<string, ClientStreamContext> _connections = new();
    private IHost? _host;
    private CancellationTokenSource? _cancellationTokenSource;
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
    public TransportType TransportType => TransportType.gRPC;
    public IReadOnlyList<ConnectionInfo> Connections => _connections.Values.Select(c => c.ConnectionInfo).ToList();

    #endregion

    #region Methods

    /// <summary>
    /// Starts the gRPC server with the provided configuration.
    /// 
    /// Configuration dictionary keys:
    /// - "Address" (string, optional): Server bind address. Defaults to constructor value.
    /// - "Port" (int, optional): Server port. Defaults to constructor value.
    /// - "EnableHttps" (bool, optional): Whether to use HTTPS. Defaults to false.
    /// - "CertificatePath" (string, optional): Path to SSL certificate for HTTPS.
    /// - "CertificatePassword" (string, optional): Password for SSL certificate.
    /// </summary>
    public async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            var address1 = configuration?.GetValueOrDefault("Address", address) as string ?? address;
            var port1 = configuration?.GetValueOrDefault("Port", port) as int? ?? port;
            var enableHttps = configuration?.GetValueOrDefault("EnableHttps", false) as bool? ?? false;
            var certPath = configuration?.GetValueOrDefault("CertificatePath") as string;
            var certPassword = configuration?.GetValueOrDefault("CertificatePassword") as string;

            _cancellationTokenSource = new CancellationTokenSource();

            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton(this);

                        // Add gRPC services - THIS WAS MISSING!
                        services.AddGrpc(options =>
                        {
                            options.MaxReceiveMessageSize = 4 * 1024 * 1024; // 4MB
                            options.MaxSendMessageSize = 4 * 1024 * 1024; // 4MB
                        });

                        // Add CORS for cross-origin requests (for web clients)
                        services.AddCors(options =>
                        {
                            options.AddDefaultPolicy(policy =>
                            {
                                policy.AllowAnyOrigin()
                                      .AllowAnyMethod()
                                      .AllowAnyHeader()
                                      .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
                            });
                        });
                    });

                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();

                        // Enable CORS before gRPC
                        app.UseCors();

                        app.UseEndpoints(endpoints =>
                        {
                            // Map the gRPC service - THIS WAS MISSING!
                            endpoints.MapGrpcService<GrpcMessagingServiceImpl>();

                            // Health check endpoint
                            endpoints.MapGet("/health", async context =>
                            {
                                context.Response.ContentType = "text/plain";
                                await context.Response.WriteAsync("gRPC server is running");
                            });

                            // Fallback for unmatched requests
                            endpoints.MapFallback(async context =>
                            {
                                context.Response.StatusCode = 404;
                                await context.Response.WriteAsync("gRPC endpoint not found");
                            });
                        });
                    });

                    var url = enableHttps ? $"https://{address1}:{port1}" : $"http://{address1}:{port1}";
                    webBuilder.UseUrls(url);

                    // Configure Kestrel properly for gRPC
                    webBuilder.UseKestrel(options =>
                    {
                        options.ListenAnyIP(port1, listenOptions =>
                        {
                            if (enableHttps && !string.IsNullOrEmpty(certPath))
                            {
                                listenOptions.UseHttps(certPath, certPassword);
                            }

                            // Configure HTTP/2 for gRPC
                            listenOptions.Protocols = HttpProtocols.Http2;
                        });

                        // For development/testing, also listen on HTTP/1.1 for health checks
                        if (!enableHttps)
                        {
                            options.ListenAnyIP(port1 + 1, listenOptions =>
                            {
                                listenOptions.Protocols = HttpProtocols.Http1;
                            });
                        }
                    });
                })
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddConsole();
                });

            _host = builder.Build();
            await _host.StartAsync(_cancellationTokenSource.Token);

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start gRPC server: {ex.Message}", ex));
            return false;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

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

            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
                _host.Dispose();
                _host = null;
            }

            IsRunning = false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Error stopping gRPC server: {ex.Message}", ex));
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
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send gRPC message: {ex.Message}", ex));
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
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to broadcast gRPC message: {ex.Message}", ex));
            return false;
        }
    }

    internal void AddConnection(ClientStreamContext context)
    {
        _connections.TryAdd(context.ConnectionInfo.Id, context);
        context.MessageReceived += OnClientMessageReceived;
        context.Disconnected += OnClientDisconnected;
        context.ErrorOccurred += OnClientErrorOccurred;
        ClientConnected?.Invoke(this, new ConnectionEventArgs(context.ConnectionInfo));
    }

    internal void RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var context))
        {
            context.MessageReceived -= OnClientMessageReceived;
            context.Disconnected -= OnClientDisconnected;
            context.ErrorOccurred -= OnClientErrorOccurred;
            ClientDisconnected?.Invoke(this, new ConnectionEventArgs(context.ConnectionInfo));
        }
    }

    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnClientDisconnected(object? sender, ConnectionEventArgs e)
    {
        RemoveConnection(e.Connection.Id);
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
                StopAsync().Wait(5000);
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }
    }

    #endregion
}

#region gRPC Service Implementation

public class GrpcMessagingServiceImpl(GrpcTransport transport) : MessagingService.MessagingServiceBase
{
    public override async Task StreamMessages(IAsyncStreamReader<GrpcMessage> requestStream,
        IServerStreamWriter<GrpcMessage> responseStream, ServerCallContext context)
    {
        var connectionInfo = new ConnectionInfo
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Unknown",
            Address = context.Peer,
            ConnectedAt = DateTime.UtcNow,
            IsActive = true
        };

        var clientContext = new ClientStreamContext(connectionInfo, responseStream, context.CancellationToken);
        transport.AddConnection(clientContext);

        try
        {
            await foreach (var grpcMessage in requestStream.ReadAllAsync(context.CancellationToken))
            {
                try
                {
                    // Update connection name from first message
                    if (connectionInfo.Name == "Unknown" && !string.IsNullOrEmpty(grpcMessage.Sender))
                    {
                        connectionInfo.Name = grpcMessage.Sender;
                    }

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

                    clientContext.OnMessageReceived(message);
                }
                catch (Exception ex)
                {
                    clientContext.OnErrorOccurred($"Message processing error: {ex.Message}", ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during cancellation
        }
        catch (Exception ex)
        {
            clientContext.OnErrorOccurred($"Stream error: {ex.Message}", ex);
        }
        finally
        {
            transport.RemoveConnection(connectionInfo.Id);
        }
    }

    public override async Task<MessageResponse> SendMessage(GrpcMessage request, ServerCallContext context)
    {
        try
        {
            var message = new Message
            {
                Id = request.Id,
                Content = request.Content,
                Sender = request.Sender,
                Receiver = request.Receiver,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(request.Timestamp).DateTime,
                Type = (MessageType)(int)request.Type,
                Metadata = request.Metadata.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
            };

            var success = await transport.SendMessageAsync(message, request.Receiver);

            return new MessageResponse
            {
                Success = success,
                ErrorMessage = success ? string.Empty : "Failed to send message"
            };
        }
        catch (Exception ex)
        {
            return new MessageResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public override Task<GrpcConnectionInfo> GetConnectionInfo(Empty request, ServerCallContext context)
    {
        var connectionInfo = new GrpcConnectionInfo
        {
            Id = Guid.NewGuid().ToString(),
            Name = Environment.MachineName,
            Address = context.Host ?? "localhost",
            ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            IsActive = true
        };

        // Add any additional properties if needed
        connectionInfo.Properties.Add("ServerVersion", "1.0.0");
        connectionInfo.Properties.Add("Protocol", "HTTP/2");

        return Task.FromResult(connectionInfo);
    }
}

#endregion

#region Client Stream Context

internal class ClientStreamContext(
    ConnectionInfo connectionInfo,
    IServerStreamWriter<GrpcMessage> responseStream,
    CancellationToken cancellationToken)
    : IDisposable
{
    private bool _disposed;

    public ConnectionInfo ConnectionInfo { get; } = connectionInfo;

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    public async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
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

            foreach (var kvp in message.Metadata)
            {
                grpcMessage.Metadata[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;
            }

            await responseStream.WriteAsync(grpcMessage);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred($"Send error: {ex.Message}", ex);
            return false;
        }
    }

    public void OnMessageReceived(Message message)
    {
        MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
    }

    public void OnErrorOccurred(string error, Exception? exception = null)
    {
        ErrorOccurred?.Invoke(this, new ErrorEventArgs(error, exception, ConnectionInfo));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
        }
    }
}

#endregion