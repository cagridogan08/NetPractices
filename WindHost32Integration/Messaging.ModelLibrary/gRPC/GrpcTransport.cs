
using System.Collections.Concurrent;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Grpc;

/// <summary>
/// Enhanced gRPC transport with comprehensive client-to-client messaging support
/// </summary>
public class GrpcTransport(string? address = "localhost", int? port = 5000) : MessageTransportBase
{
    #region Fields

    private readonly ConcurrentDictionary<string, GrpcClientContext> _clientStreams = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _clientGroups = new();
    private IHost? _host;
    private CancellationTokenSource? _cancellationTokenSource;
    internal DateTime _startTime;
    #endregion

    #region Properties
    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.gRPC;
    public override IReadOnlyList<ConnectionInfo> Connections =>
        _clientStreams.Values.Select(c => c.ConnectionInfo).ToList();
    #endregion

    #region Transport Implementation
    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            var address1 = configuration?.GetValueOrDefault("Address", "localhost") as string ?? address;
            var port1 = configuration?.GetValueOrDefault("Port", 5000) as int? ?? port;
            var enableHttps = configuration?.GetValueOrDefault("EnableHttps", false) as bool? ?? false;
            var certPath = configuration?.GetValueOrDefault("CertificatePath") as string;
            var certPassword = configuration?.GetValueOrDefault("CertificatePassword") as string;
            var maxReceiveSize = configuration?.GetValueOrDefault("MaxReceiveMessageSize", 16 * 1024 * 1024) as int? ?? 16 * 1024 * 1024;
            var maxSendSize = configuration?.GetValueOrDefault("MaxSendMessageSize", 16 * 1024 * 1024) as int? ?? 16 * 1024 * 1024;

            _cancellationTokenSource = new CancellationTokenSource();
            _startTime = DateTime.UtcNow;

            var builder = Host.CreateDefaultBuilder()
      .ConfigureWebHostDefaults(webBuilder =>
      {
          webBuilder.ConfigureServices(services =>
          {
              services.AddSingleton(this);

              // Add gRPC services - THIS WAS MISSING!
              services.AddGrpc(options =>
              {
                  options.MaxReceiveMessageSize = maxReceiveSize; // 4MB
                  options.MaxSendMessageSize = maxSendSize; // 4MB
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
                  endpoints.MapGrpcService<GrpcMessagingServiceImplementation>();

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
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to start gRPC server: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            // Gracefully close all client streams
            var closeTasks = _clientStreams.Values.Select(context =>
                Task.Run(context.CompleteStream));

            try
            {
                await Task.WhenAll(closeTasks);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"Error closing client streams: {ex.Message}", ex));
            }

            _clientStreams.Clear();
            lock (_lockObject)
            {
                _groups.Clear();
            }
            _clientGroups.Clear();

            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(10));
                _host.Dispose();
                _host = null;
            }

            IsRunning = false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error stopping gRPC server: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionId))
                return await BroadcastMessageAsync(message);

            if (_clientStreams.TryGetValue(connectionId, out var clientContext))
            {
                return await clientContext.SendMessageAsync(message);
            }

            return false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send gRPC message: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            var tasks = _clientStreams.Values.Select(context => context.SendMessageAsync(message));
            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast gRPC message: {ex.Message}", ex));
            return false;
        }
    }

    protected override string GetClientAddress(object transportSpecificData)
    {
        return transportSpecificData is ServerCallContext context
            ? context.Peer
            : "Unknown";
    }
    #endregion

    #region Client Management
    internal void AddClientStream(string clientId, string clientName, GrpcClientContext context)
    {
        _clientStreams[clientId] = context;
        RegisterClient(clientId, clientName, context.CallContext);

        context.StreamClosed += (sender, e) => RemoveClientStream(clientId);
        context.MessageReceived += OnClientMessageReceived;
        context.ErrorOccurred += OnClientErrorOccurred;
    }

    internal void RemoveClientStream(string clientId)
    {
        if (_clientStreams.TryRemove(clientId, out var context))
        {
            // Remove from all groups
            RemoveClientFromAllGroups(clientId);
            UnregisterClient(clientId);
        }
    }

    internal async Task<List<ClientInfo>> GetOnlineClientsAsync()
    {
        return _clientStreams.Values.Select(context => new ClientInfo
        {
            Id = context.ClientId,
            Name = context.ClientName,
            DisplayName = context.ClientName,
            Address = context.CallContext.Peer,
            IsOnline = true,

        }).ToList();
    }
    #endregion

    #region Group Management
    internal async Task<bool> CreateGroupAsync(string groupName, string creatorId)
    {
        lock (_lockObject)
        {
            if (_groups.ContainsKey(groupName))
                return false;

            _groups[groupName] = new HashSet<string> { creatorId };

            if (!_clientGroups.TryGetValue(creatorId, out var clientGroups))
            {
                clientGroups = new HashSet<string>();
                _clientGroups[creatorId] = clientGroups;
            }
            clientGroups.Add(groupName);
        }

        return true;
    }

    internal async Task<bool> JoinGroupAsync(string groupName, string clientId)
    {
        lock (_lockObject)
        {
            if (!_groups.TryGetValue(groupName, out var groupMembers))
            {
                groupMembers = new HashSet<string>();
                _groups[groupName] = groupMembers;
            }

            groupMembers.Add(clientId);

            if (!_clientGroups.TryGetValue(clientId, out var clientGroups))
            {
                clientGroups = new HashSet<string>();
                _clientGroups[clientId] = clientGroups;
            }
            clientGroups.Add(groupName);
        }

        return true;
    }

    internal async Task<bool> LeaveGroupAsync(string groupName, string clientId)
    {
        lock (_lockObject)
        {
            var success = false;

            if (_groups.TryGetValue(groupName, out var groupMembers))
            {
                success = groupMembers.Remove(clientId);

                if (groupMembers.Count == 0)
                {
                    _groups.TryRemove(groupName, out _);
                }
            }

            if (_clientGroups.TryGetValue(clientId, out var clientGroups))
            {
                clientGroups.Remove(groupName);
            }

            return success;
        }
    }

    internal async Task<bool> SendGroupMessageAsync(string groupName, Message message)
    {
        HashSet<string>? groupMembers;
        lock (_lockObject)
        {
            if (!_groups.TryGetValue(groupName, out groupMembers))
                return false;

            groupMembers = new HashSet<string>(groupMembers); // Create copy to avoid locking during async operations
        }

        var tasks = new List<Task<bool>>();
        foreach (var memberId in groupMembers.Where(id => id != message.Sender))
        {
            if (_clientStreams.TryGetValue(memberId, out var context))
            {
                var groupMessage = CreateGroupMessage(message, groupName, memberId);
                tasks.Add(context.SendMessageAsync(groupMessage));
            }
        }

        if (tasks.Count == 0) return false;

        var results = await Task.WhenAll(tasks);
        return results.Any(r => r);
    }

    internal List<string> GetGroupMembers(string groupName)
    {
        lock (_lockObject)
        {
            return _groups.TryGetValue(groupName, out var members)
                ? members.ToList()
                : new List<string>();
        }
    }

    private void RemoveClientFromAllGroups(string clientId)
    {
        lock (_lockObject)
        {
            if (_clientGroups.TryGetValue(clientId, out var groups))
            {
                foreach (var groupName in groups.ToList())
                {
                    if (_groups.TryGetValue(groupName, out var groupMembers))
                    {
                        groupMembers.Remove(clientId);
                        if (groupMembers.Count == 0)
                        {
                            _groups.TryRemove(groupName, out _);
                        }
                    }
                }
                _clientGroups.TryRemove(clientId, out _);
            }
        }
    }

    private Message CreateGroupMessage(Message originalMessage, string groupName, string receiverId)
    {
        return new Message
        {
            Id = originalMessage.Id,
            Content = originalMessage.Content,
            Sender = originalMessage.Sender,
            Receiver = receiverId,
            Type = originalMessage.Type,
            Timestamp = originalMessage.Timestamp,
            Priority = originalMessage.Priority,
            ReplyToId = originalMessage.ReplyToId,
            RequiresAcknowledgment = originalMessage.RequiresAcknowledgment,
            ExpiresIn = originalMessage.ExpiresIn,
            Tags = originalMessage.Tags,
            Metadata = new Dictionary<string, object>(originalMessage.Metadata)
            {
                ["GroupName"] = groupName,
                ["IsGroupMessage"] = true,
                ["OriginalReceiver"] = originalMessage.Receiver
            }
        };
    }
    #endregion

    #region Event Handlers
    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        HandleReceivedMessage(e.Message, e.Message.Sender);
    }

    private void OnClientErrorOccurred(object? sender, ErrorEventArgs e)
    {
        OnErrorOccurred(e);
    }
    #endregion
}

/// <summary>
/// Enhanced gRPC service implementation
/// </summary>
public class GrpcMessagingServiceImplementation(GrpcTransport transport) : GrpcMessagingService.GrpcMessagingServiceBase
{
    #region Streaming Methods
    public override async Task StreamMessages(IAsyncStreamReader<GrpcMessage> requestStream,
        IServerStreamWriter<GrpcMessage> responseStream, ServerCallContext context)
    {
        var clientId = "";
        var clientName = "";
        GrpcClientContext? clientContext = null;

        try
        {
            // Read the first message for client registration
            if (await requestStream.MoveNext())
            {
                var firstMessage = requestStream.Current;
                clientId = firstMessage.Sender;
                clientName = firstMessage.Sender;

                clientContext = new GrpcClientContext(clientId, clientName, responseStream, context);
                transport.AddClientStream(clientId, clientName, clientContext);

                // Send welcome message
                var welcomeMessage = new GrpcMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = $"Welcome {clientName}! You are now connected to the gRPC messaging server.",
                    Sender = "System",
                    Receiver = clientName,
                    Type = GrpcMessageType.System,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                await responseStream.WriteAsync(welcomeMessage);
            }

            // Process incoming messages
            do
            {
                var grpcMessage = requestStream.Current;

                try
                {
                    var message = ConvertFromGrpcMessage(grpcMessage);
                    clientContext?.OnMessageReceived(message);
                }
                catch (Exception ex)
                {
                    clientContext?.OnErrorOccurred($"Message processing error: {ex.Message}", ex);
                }
            }
            while (await requestStream.MoveNext());
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Client disconnected - this is normal
        }
        catch (Exception ex)
        {
            clientContext?.OnErrorOccurred($"Stream error: {ex.Message}", ex);
        }
        finally
        {
            if (!string.IsNullOrEmpty(clientId))
            {
                transport.RemoveClientStream(clientId);
            }
        }
    }
    #endregion

    #region Request-Response Methods
    public override async Task<MessageResponse> SendMessage(GrpcMessage request, ServerCallContext context)
    {
        try
        {
            var message = ConvertFromGrpcMessage(request);
            var success = await transport.SendMessageAsync(message, request.Receiver);

            return new MessageResponse
            {
                Success = success,
                MessageId = request.Id,
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

    public override async Task<RegistrationResponse> RegisterClient(ClientRegistration request, ServerCallContext context)
    {
        try
        {
            // In streaming mode, registration is handled automatically
            return new RegistrationResponse
            {
                Success = true,
                Message = "Use StreamMessages for full functionality",
                AssignedId = request.ClientId
            };
        }
        catch (Exception ex)
        {
            return new RegistrationResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public override async Task<ClientList> GetOnlineClients(Empty request, ServerCallContext context)
    {
        try
        {
            var clients = await transport.GetOnlineClientsAsync();
            var grpcClients = clients.Select(c => new ClientInfo
            {
                Id = c.Id,
                Name = c.Name,
                DisplayName = c.DisplayName,
                Address = c.Address,
                IsOnline = c.IsOnline
            });

            return new ClientList
            {
                Clients = { grpcClients }
            };
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
    }

    public override async Task<GroupResponse> CreateGroup(GroupRequest request, ServerCallContext context)
    {
        try
        {
            var success = await transport.CreateGroupAsync(request.GroupName, request.ClientId);
            return new GroupResponse
            {
                Success = success,
                Message = success ? "Group created successfully" : "Group already exists or creation failed"
            };
        }
        catch (Exception ex)
        {
            return new GroupResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public override async Task<GroupResponse> JoinGroup(GroupRequest request, ServerCallContext context)
    {
        try
        {
            var success = await transport.JoinGroupAsync(request.GroupName, request.ClientId);
            var members = transport.GetGroupMembers(request.GroupName);

            return new GroupResponse
            {
                Success = success,
                Message = success ? "Joined group successfully" : "Failed to join group",
                Members = { members }
            };
        }
        catch (Exception ex)
        {
            return new GroupResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public override async Task<GroupResponse> LeaveGroup(GroupRequest request, ServerCallContext context)
    {
        try
        {
            var success = await transport.LeaveGroupAsync(request.GroupName, request.ClientId);
            return new GroupResponse
            {
                Success = success,
                Message = success ? "Left group successfully" : "Failed to leave group or not in group"
            };
        }
        catch (Exception ex)
        {
            return new GroupResponse
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public override async Task<MessageResponse> SendGroupMessage(GroupMessage request, ServerCallContext context)
    {
        try
        {
            var message = ConvertFromGrpcMessage(request.Message);
            var success = await transport.SendGroupMessageAsync(request.GroupName, message);

            return new MessageResponse
            {
                Success = success,
                MessageId = request.Message.Id,
                ErrorMessage = success ? string.Empty : "Failed to send group message"
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

    public override async Task<HealthResponse> HealthCheck(Empty request, ServerCallContext context)
    {
        var uptime = DateTime.UtcNow - transport._startTime;

        return new HealthResponse
        {
            Healthy = transport.IsRunning,
            Version = "1.0.0",
            ActiveConnections = transport.Connections.Count,
            UptimeSeconds = (long)uptime.TotalSeconds
        };
    }
    #endregion

    #region Helper Methods
    private Message ConvertFromGrpcMessage(GrpcMessage grpcMessage)
    {
        return new Message
        {
            Id = grpcMessage.Id,
            Content = grpcMessage.Content,
            Sender = grpcMessage.Sender,
            Receiver = grpcMessage.Receiver,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(grpcMessage.Timestamp).DateTime,
            Type = (MessageType)(int)grpcMessage.Type,
            Priority = (MessagePriority)(int)grpcMessage.Priority,
            ReplyToId = grpcMessage.ReplyToId,
            RequiresAcknowledgment = grpcMessage.RequiresAcknowledgment,
            Tags = grpcMessage.Tags?.ToArray(),
            Metadata = grpcMessage.Metadata.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
        };
    }

    private GrpcMessage ConvertToGrpcMessage(Message message)
    {
        var grpcMessage = new GrpcMessage
        {
            Id = message.Id,
            Content = message.Content,
            Sender = message.Sender,
            Receiver = message.Receiver,
            Timestamp = ((DateTimeOffset)message.Timestamp).ToUnixTimeSeconds(),
            Type = (GrpcMessageType)(int)message.Type,
            Priority = (GrpcMessagePriority)(int)message.Priority,
            ReplyToId = message.ReplyToId ?? string.Empty,
            RequiresAcknowledgment = message.RequiresAcknowledgment
        };

        if (message.Tags != null)
            grpcMessage.Tags.AddRange(message.Tags);

        foreach (var kvp in message.Metadata)
            grpcMessage.Metadata[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;

        return grpcMessage;
    }
    #endregion
}

/// <summary>
/// Enhanced client context for managing gRPC streaming connections
/// </summary>
internal class GrpcClientContext : IDisposable
{
    #region Fields
    private readonly IServerStreamWriter<GrpcMessage> _responseStream;
    private readonly SemaphoreSlim _streamSemaphore = new(1, 1);
    private volatile bool _disposed;
    #endregion

    #region Properties
    public string ClientId { get; }
    public string ClientName { get; }
    public ServerCallContext CallContext { get; }
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    public bool IsStreamActive => !_disposed && !CallContext.CancellationToken.IsCancellationRequested;

    public ConnectionInfo ConnectionInfo { get; }
    #endregion

    #region Events
    public event EventHandler<EventArgs>? StreamClosed;
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;
    #endregion

    #region Constructor
    public GrpcClientContext(string clientId, string clientName,
        IServerStreamWriter<GrpcMessage> responseStream, ServerCallContext callContext)
    {
        ClientId = clientId;
        ClientName = clientName;
        _responseStream = responseStream;
        CallContext = callContext;

        ConnectionInfo = new ConnectionInfo
        {
            Id = clientId,
            Name = clientName,
            Address = callContext.Peer,
            ConnectedAt = ConnectedAt,
            IsActive = true,
            Properties = new Dictionary<string, object>
            {
                ["Protocol"] = "gRPC",
                ["Method"] = callContext.Method,
                ["UserAgent"] = callContext.RequestHeaders.GetValue("user-agent") ?? "Unknown"
            }
        };

        // Monitor cancellation
        CallContext.CancellationToken.Register(() =>
        {
            if (!_disposed)
            {
                StreamClosed?.Invoke(this, EventArgs.Empty);
            }
        });
    }
    #endregion

    #region Message Handling
    public async Task<bool> SendMessageAsync(Message message)
    {
        if (_disposed || !IsStreamActive)
            return false;

        await _streamSemaphore.WaitAsync();
        try
        {
            var grpcMessage = ConvertToGrpcMessage(message);
            await _responseStream.WriteAsync(grpcMessage);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred($"Failed to send message: {ex.Message}", ex);
            return false;
        }
        finally
        {
            _streamSemaphore.Release();
        }
    }

    public void OnMessageReceived(Message message)
    {
        if (!_disposed)
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
        }
    }

    public void OnErrorOccurred(string error, Exception? exception = null)
    {
        if (!_disposed)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs(error, exception, ConnectionInfo));
        }
    }
    #endregion

    #region Stream Management
    public void CompleteStream()
    {
        if (!_disposed)
        {
            try
            {
                // The stream will be completed automatically when the method exits
                StreamClosed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                OnErrorOccurred($"Error completing stream: {ex.Message}", ex);
            }
        }
    }
    #endregion

    #region Helper Methods
    private GrpcMessage ConvertToGrpcMessage(Message message)
    {
        var grpcMessage = new GrpcMessage
        {
            Id = message.Id,
            Content = message.Content,
            Sender = message.Sender,
            Receiver = message.Receiver,
            Timestamp = ((DateTimeOffset)message.Timestamp).ToUnixTimeSeconds(),
            Type = (GrpcMessageType)(int)message.Type,
            Priority = (GrpcMessagePriority)(int)message.Priority,
            ReplyToId = message.ReplyToId ?? string.Empty,
            RequiresAcknowledgment = message.RequiresAcknowledgment
        };

        if (message.Tags != null)
            grpcMessage.Tags.AddRange(message.Tags);

        foreach (var kvp in message.Metadata)
            grpcMessage.Metadata[kvp.Key] = kvp.Value?.ToString() ?? string.Empty;

        return grpcMessage;
    }
    #endregion

    #region Disposal
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _streamSemaphore?.Dispose();
        }
    }
    #endregion
}