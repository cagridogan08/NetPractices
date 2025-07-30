using Messaging.ModelLibrary.Abstract;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Messaging.ModelLibrary.SignalR;

/// <summary>
/// Enhanced SignalR transport with comprehensive client-to-client messaging support
/// </summary>
public class SignalRTransport(string address = "localhost", int port = 5003, string path = "/messagingHub")
    : MessageTransportBase
{
    #region Fields

    private readonly ConcurrentDictionary<string, EnhancedSignalRClientInfo> _clientConnections = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _groups = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _clientGroups = new();
    private IHost? _host;
    private CancellationTokenSource? _cancellationTokenSource;
    private DateTime _startTime;
    #endregion

    #region Properties
    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.SignalR;
    public override IReadOnlyList<ConnectionInfo> Connections =>
        _clientConnections.Values.Select(c => c.ConnectionInfo).ToList();

    // Additional properties for monitoring
    public int ActiveGroups => _groups.Count;
    public TimeSpan Uptime => IsRunning ? DateTime.UtcNow - _startTime : TimeSpan.Zero;
    #endregion

    #region Transport Implementation
    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            var address1 = configuration?.GetValueOrDefault("Address", address) as string ?? address;
            var port1 = configuration?.GetValueOrDefault("Port", port) as int? ?? port;
            var hubPath = configuration?.GetValueOrDefault("HubPath", path) as string ?? path;
            var enableHttps = configuration?.GetValueOrDefault("EnableHttps", false) as bool? ?? false;
            var certPath = configuration?.GetValueOrDefault("CertificatePath") as string;
            var certPassword = configuration?.GetValueOrDefault("CertificatePassword") as string;
            var enableCors = configuration?.GetValueOrDefault("EnableCors", true) as bool? ?? true;
            var corsOrigins = configuration?.GetValueOrDefault("CorsOrigins", new[] { "*" }) as string[] ?? new[] { "*" };
            var enableDetailedErrors = configuration?.GetValueOrDefault("EnableDetailedErrors", false) as bool? ?? false;
            var maxBufferSize = configuration?.GetValueOrDefault("MaxBufferSize", 32 * 1024) as int? ?? 32 * 1024;
            var keepAliveInterval = configuration?.GetValueOrDefault("KeepAliveInterval", 15) as int? ?? 15;
            var clientTimeoutInterval = configuration?.GetValueOrDefault("ClientTimeoutInterval", 30) as int? ?? 30;

            _cancellationTokenSource = new CancellationTokenSource();
            _startTime = DateTime.UtcNow;

            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        // Register the transport instance
                        services.AddSingleton(this);

                        // Add SignalR services with enhanced configuration
                        var signalRBuilder = services.AddSignalR(options =>
                        {
                            options.EnableDetailedErrors = enableDetailedErrors;
                            options.MaximumReceiveMessageSize = maxBufferSize;
                            options.StreamBufferCapacity = 20;
                            options.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveInterval);
                            options.ClientTimeoutInterval = TimeSpan.FromSeconds(clientTimeoutInterval);
                            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
                            options.MaximumParallelInvocationsPerClient = 10;
                        });


                        // Add JSON protocol with custom options
                        signalRBuilder.AddJsonProtocol(options =>
                        {
                            options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                            options.PayloadSerializerOptions.WriteIndented = false;
                        });

                        // Add CORS if enabled
                        if (enableCors)
                        {
                            services.AddCors(options =>
                            {
                                options.AddPolicy("SignalRCorsPolicy", policy =>
                                {
                                    if (corsOrigins.Contains("*"))
                                    {
                                        policy.SetIsOriginAllowed(_ => true);
                                    }
                                    else
                                    {
                                        policy.WithOrigins(corsOrigins);
                                    }

                                    policy.AllowAnyHeader()
                                          .AllowAnyMethod()
                                          .AllowCredentials();
                                });
                            });
                        }

                        // Add health checks
                        services.AddHealthChecks();

                        // Add logging
                        services.AddLogging(builder =>
                        {
                            builder.AddConsole();
                            builder.SetMinimumLevel(LogLevel.Information);
                        });
                    });

                    webBuilder.Configure(app =>
                    {
                        if (enableCors)
                        {
                            app.UseCors("SignalRCorsPolicy");
                        }

                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            // Map the enhanced SignalR hub
                            endpoints.MapHub<EnhancedMessagingHub>(hubPath);

                            // Health check endpoint
                            endpoints.MapHealthChecks("/health");

                            // Status endpoint for monitoring
                            endpoints.MapGet("/status", async context =>
                            {
                                var status = new
                                {
                                    Status = "Healthy",
                                    Transport = "SignalR",
                                    HubPath = hubPath,
                                    ActiveConnections = _clientConnections.Count,
                                    ActiveGroups = _groups.Count,
                                    Uptime = DateTime.UtcNow - _startTime,
                                    Version = "1.0"
                                };

                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(status));
                            });

                            // Hub info endpoint
                            endpoints.MapGet(hubPath + "/info", async context =>
                            {
                                var info = new
                                {
                                    HubName = "EnhancedMessagingHub",
                                    MaxMessageSize = maxBufferSize,
                                    KeepAliveInterval = keepAliveInterval,
                                    ClientTimeout = clientTimeoutInterval
                                };

                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(info));
                            });
                        });
                    });

                    // Configure server URLs
                    var protocol = enableHttps ? "https" : "http";
                    var url = $"{protocol}://{address1}:{port1}";
                    webBuilder.UseUrls(url);

                    // Configure Kestrel for HTTPS if needed
                    if (enableHttps && !string.IsNullOrEmpty(certPath))
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.ConfigureHttpsDefaults(httpsOptions =>
                            {
                                if (!string.IsNullOrEmpty(certPassword))
                                {
                                    httpsOptions.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPassword);
                                }
                                else
                                {
                                    httpsOptions.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath);
                                }
                            });
                        });
                    }
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
            OnErrorOccurred(new ErrorEventArgs($"Failed to start SignalR server: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            // Notify all clients about server shutdown
            if (_host != null)
            {
                var hubContext = _host.Services.GetService<IHubContext<EnhancedMessagingHub>>();
                if (hubContext != null)
                {
                    try
                    {
                        await hubContext.Clients.All.SendAsync("ServerShutdown", "Server is shutting down", CancellationToken.None);
                        await Task.Delay(1000); // Give clients time to receive the message
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(new ErrorEventArgs($"Error sending shutdown notification: {ex.Message}", ex));
                    }
                }
            }

            // Notify about disconnections
            foreach (var client in _clientConnections.Values)
            {
                OnClientDisconnected(new ConnectionEventArgs(client.ConnectionInfo));
            }

            _clientConnections.Clear();
            _groups.Clear();
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
            OnErrorOccurred(new ErrorEventArgs($"Error stopping SignalR server: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (!IsRunning || _host == null)
                return false;

            var hubContext = _host.Services.GetRequiredService<IHubContext<EnhancedMessagingHub>>();

            if (string.IsNullOrEmpty(connectionId))
            {
                return await BroadcastMessageAsync(message);
            }

            if (_clientConnections.ContainsKey(connectionId))
            {
                await hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", message);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send SignalR message: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            if (!IsRunning || _host == null)
                return false;

            var hubContext = _host.Services.GetRequiredService<IHubContext<EnhancedMessagingHub>>();
            await hubContext.Clients.All.SendAsync("ReceiveMessage", message);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast SignalR message: {ex.Message}", ex));
            return false;
        }
    }

    protected override string GetClientAddress(object transportSpecificData)
    {
        return transportSpecificData is HubCallerContext context
            ? context.GetHttpContext()?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown"
            : "Unknown";
    }
    #endregion

    #region Client Management
    internal void AddConnection(string connectionId, string userName, HubCallerContext context)
    {
        var connectionInfo = new ConnectionInfo
        {
            Id = userName, // Use userName as logical ID for routing
            Name = userName,
            Address = GetClientAddress(context),
            ConnectedAt = DateTime.UtcNow,
            IsActive = true,
            Properties = new Dictionary<string, object>
            {
                ["SignalRConnectionId"] = connectionId,
                ["UserAgent"] = context.GetHttpContext()?.Request?.Headers["User-Agent"].ToString() ?? "Unknown",
                ["Protocol"] = "SignalR"
            }
        };

        var clientInfo = new EnhancedSignalRClientInfo(connectionInfo, connectionId, context);
        _clientConnections[userName] = clientInfo;

        RegisterClient(userName, userName, context);
        OnClientDisconnected(new ConnectionEventArgs(connectionInfo));
    }

    internal void RemoveConnection(string connectionId)
    {
        var clientToRemove = _clientConnections.Values.FirstOrDefault(c => c.SignalRConnectionId == connectionId);
        if (clientToRemove != null)
        {
            var userName = clientToRemove.ConnectionInfo.Name;

            if (_clientConnections.TryRemove(userName, out var removedClient))
            {
                // Remove from all groups
                RemoveClientFromAllGroups(userName);
                UnregisterClient(userName);
                OnClientDisconnected(new ConnectionEventArgs(removedClient.ConnectionInfo));
            }
        }
    }

    internal void OnMessageReceived(Message message, string connectionId)
    {
        var clientInfo = _clientConnections.Values.FirstOrDefault(c => c.SignalRConnectionId == connectionId);
        if (clientInfo != null)
        {
            clientInfo.UpdateLastActivity();
            HandleReceivedMessage(message, message.Sender);
        }
    }

    internal void OnError(string error, string connectionId)
    {
        var clientInfo = _clientConnections.Values.FirstOrDefault(c => c.SignalRConnectionId == connectionId);
        OnErrorOccurred(new ErrorEventArgs(error, null, clientInfo?.ConnectionInfo));
    }

    internal List<EnhancedSignalRClientInfo> GetOnlineClients()
    {
        return _clientConnections.Values.ToList();
    }
    #endregion

    #region Group Management
    internal async Task<bool> CreateGroupAsync(string groupName, string creatorName)
    {
        lock (_lockObject)
        {
            if (_groups.ContainsKey(groupName))
                return false;

            _groups[groupName] = new HashSet<string> { creatorName };

            if (!_clientGroups.TryGetValue(creatorName, out var clientGroups))
            {
                clientGroups = new HashSet<string>();
                _clientGroups[creatorName] = clientGroups;
            }
            clientGroups.Add(groupName);
        }

        return true;
    }

    internal async Task<bool> JoinGroupAsync(string groupName, string userName, string connectionId)
    {
        try
        {
            if (_host == null) return false;

            var hubContext = _host.Services.GetRequiredService<IHubContext<EnhancedMessagingHub>>();

            // Add to SignalR group
            await hubContext.Groups.AddToGroupAsync(connectionId, groupName);

            // Add to our tracking
            lock (_lockObject)
            {
                if (!_groups.TryGetValue(groupName, out var groupMembers))
                {
                    groupMembers = new HashSet<string>();
                    _groups[groupName] = groupMembers;
                }

                groupMembers.Add(userName);

                if (!_clientGroups.TryGetValue(userName, out var clientGroups))
                {
                    clientGroups = new HashSet<string>();
                    _clientGroups[userName] = clientGroups;
                }
                clientGroups.Add(groupName);
            }

            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error joining group {groupName}: {ex.Message}", ex));
            return false;
        }
    }

    internal async Task<bool> LeaveGroupAsync(string groupName, string userName, string connectionId)
    {
        try
        {
            if (_host == null) return false;

            var hubContext = _host.Services.GetRequiredService<IHubContext<EnhancedMessagingHub>>();

            // Remove from SignalR group
            await hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName);

            // Remove from our tracking
            lock (_lockObject)
            {
                var success = false;

                if (_groups.TryGetValue(groupName, out var groupMembers))
                {
                    success = groupMembers.Remove(userName);

                    if (groupMembers.Count == 0)
                    {
                        _groups.TryRemove(groupName, out _);
                    }
                }

                if (_clientGroups.TryGetValue(userName, out var clientGroups))
                {
                    clientGroups.Remove(groupName);
                }

                return success;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error leaving group {groupName}: {ex.Message}", ex));
            return false;
        }
    }

    internal async Task<bool> SendGroupMessageAsync(string groupName, Message message)
    {
        try
        {
            if (_host == null) return false;

            var hubContext = _host.Services.GetRequiredService<IHubContext<EnhancedMessagingHub>>();

            // Create group message with metadata
            var groupMessage = CreateGroupMessage(message, groupName);

            await hubContext.Clients.Group(groupName).SendAsync("ReceiveMessage", groupMessage);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error sending group message: {ex.Message}", ex));
            return false;
        }
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

    internal List<string> GetClientGroups(string userName)
    {
        lock (_lockObject)
        {
            return _clientGroups.TryGetValue(userName, out var groups)
                ? groups.ToList()
                : new List<string>();
        }
    }

    private void RemoveClientFromAllGroups(string userName)
    {
        lock (_lockObject)
        {
            if (_clientGroups.TryGetValue(userName, out var groups))
            {
                foreach (var groupName in groups.ToList())
                {
                    if (_groups.TryGetValue(groupName, out var groupMembers))
                    {
                        groupMembers.Remove(userName);
                        if (groupMembers.Count == 0)
                        {
                            _groups.TryRemove(groupName, out _);
                        }
                    }
                }
                _clientGroups.TryRemove(userName, out _);
            }
        }
    }

    private Message CreateGroupMessage(Message originalMessage, string groupName)
    {
        return new Message
        {
            Id = originalMessage.Id,
            Content = originalMessage.Content,
            Sender = originalMessage.Sender,
            Receiver = originalMessage.Receiver,
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

    #region Statistics and Monitoring
    internal SignalRServerStatistics GetStatistics()
    {
        return new SignalRServerStatistics
        {
            ActiveConnections = _clientConnections.Count,
            ActiveGroups = _groups.Count,
            TotalMessages = 0, // Could be tracked with a counter
            Uptime = Uptime,
            IsRunning = IsRunning,
            HubPath = path,
            ServerAddress = $"{address}:{port}"
        };
    }
    #endregion
}

/// <summary>
/// Enhanced SignalR Hub implementation with comprehensive messaging features
/// </summary>
public class EnhancedMessagingHub(SignalRTransport transport, ILogger<EnhancedMessagingHub> logger)
    : Hub
{
    #region Connection Management
    public async Task JoinAsync(string userName)
    {
        try
        {
            logger.LogInformation("User {UserName} joining with connection {ConnectionId}", userName, Context.ConnectionId);

            transport.AddConnection(Context.ConnectionId, userName, Context);

            // Send welcome message
            await Clients.Caller.SendAsync("SystemMessage", $"Welcome {userName}! You are now connected to the messaging server.");

            // Notify other clients
            await Clients.Others.SendAsync("UserConnected", Context.ConnectionId, userName);

            // Send current online users list
            var onlineUsers = transport.GetOnlineClients().Select(c => new { c.ConnectionInfo.Id, c.ConnectionInfo.Name }).ToList();
            await Clients.Caller.SendAsync("OnlineUsersList", onlineUsers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in JoinAsync for user {UserName}", userName);
            transport.OnError($"Join error: {ex.Message}", Context.ConnectionId);
        }
    }

    public async Task LeaveAsync(string userName)
    {
        try
        {
            logger.LogInformation("User {UserName} leaving with connection {ConnectionId}", userName, Context.ConnectionId);

            transport.RemoveConnection(Context.ConnectionId);

            // Notify other clients
            await Clients.Others.SendAsync("UserDisconnected", Context.ConnectionId, userName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in LeaveAsync for user {UserName}", userName);
            transport.OnError($"Leave error: {ex.Message}", Context.ConnectionId);
        }
    }
    #endregion

    #region Message Handling
    public async Task SendMessageAsync(Message message)
    {
        try
        {
            logger.LogDebug("Received message from {Sender} to {Receiver}", message.Sender, message.Receiver);

            // Update message sender if not set
            if (string.IsNullOrEmpty(message.Sender))
            {
                var clientInfo = transport.GetOnlineClients().FirstOrDefault(c => c.SignalRConnectionId == Context.ConnectionId);
                message.Sender = clientInfo?.ConnectionInfo.Name ?? Context.UserIdentifier ?? Context.ConnectionId;
            }

            // Handle different message types
            if (string.IsNullOrEmpty(message.Receiver) || message.Receiver == "*")
            {
                // Broadcast message
                await Clients.Others.SendAsync("ReceiveMessage", message);
            }
            else
            {
                // Direct message - find target client
                var targetClient = transport.GetOnlineClients().FirstOrDefault(c => c.ConnectionInfo.Name == message.Receiver);
                if (targetClient != null)
                {
                    await Clients.Client(targetClient.SignalRConnectionId).SendAsync("ReceiveMessage", message);

                    // Send delivery confirmation to sender
                    await Clients.Caller.SendAsync("MessageDelivered", message.Id, message.Receiver);
                }
                else
                {
                    // Target not found
                    await Clients.Caller.SendAsync("MessageError", message.Id, $"User '{message.Receiver}' is not online");
                }
            }

            // Notify transport for processing
            transport.OnMessageReceived(message, Context.ConnectionId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SendMessageAsync");
            transport.OnError($"Send message error: {ex.Message}", Context.ConnectionId);
            await Clients.Caller.SendAsync("MessageError", message?.Id ?? "unknown", ex.Message);
        }
    }

    public async Task SendDirectMessageAsync(string recipientName, string content, MessageType messageType = MessageType.Text)
    {
        try
        {
            var senderInfo = transport.GetOnlineClients().FirstOrDefault(c => c.SignalRConnectionId == Context.ConnectionId);
            var senderName = senderInfo?.ConnectionInfo.Name ?? "Unknown";

            var message = new Message
            {
                Content = content,
                Sender = senderName,
                Receiver = recipientName,
                Type = messageType,
                Timestamp = DateTime.UtcNow
            };

            await SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in SendDirectMessageAsync");
            await Clients.Caller.SendAsync("MessageError", "direct_message", ex.Message);
        }
    }

    public async Task BroadcastMessageAsync(string content, MessageType messageType = MessageType.Text)
    {
        try
        {
            var senderInfo = transport.GetOnlineClients().FirstOrDefault(c => c.SignalRConnectionId == Context.ConnectionId);
            var senderName = senderInfo?.ConnectionInfo.Name ?? "Unknown";

            var message = new Message
            {
                Content = content,
                Sender = senderName,
                Receiver = "*",
                Type = messageType,
                Timestamp = DateTime.UtcNow
            };

            await SendMessageAsync(message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in BroadcastMessageAsync");
            await Clients.Caller.SendAsync("MessageError", "broadcast", ex.Message);
        }
    }
    #endregion

    #region Group Management
    public async Task CreateGroupAsync(string groupName)
    {
        try
        {
            var senderInfo = transport.GetOnlineClients().FirstOrDefault(c => c.SignalRConnectionId == Context.ConnectionId);
            var senderName = senderInfo?.ConnectionInfo.Name ?? "Unknown";

            var success = await transport.CreateGroupAsync(groupName, senderName);

            if (success)
            {
                await transport.JoinGroupAsync(groupName, senderName, Context.ConnectionId);
                await Clients.Caller.SendAsync("GroupCreated", groupName);

                logger.LogInformation("Group {GroupName} created by {UserName}", groupName, senderName);
            }
            else
            {
                await Clients.Caller.SendAsync("GroupError", groupName, "Group already exists");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating group {GroupName}", groupName);
            await Clients.Caller.SendAsync("GroupError", groupName, ex.Message);
        }
    }

    public async Task JoinGroupAsync(string groupName)
    {
        try
        {
            var senderInfo = transport.GetOnlineClients().FirstOrDefault(c => c.SignalRConnectionId == Context.ConnectionId);
            var senderName = senderInfo?.ConnectionInfo.Name ?? "Unknown";

            var success = await transport.JoinGroupAsync(groupName, senderName, Context.ConnectionId);

            if (success)
            {
                await Clients.Caller.SendAsync("GroupJoined", groupName);
                await Clients.Group(groupName).SendAsync("UserJoinedGroup", senderName, groupName);

                // Send group member list
                var members = transport.GetGroupMembers(groupName);
                await Clients.Caller.SendAsync("GroupMembers", groupName, members);

                logger.LogInformation("User {UserName} joined group {GroupName}", senderName, groupName);
            }
            else
            {
                await Clients.Caller.SendAsync("GroupError", groupName, "Failed to join group");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error joining group {GroupName}", groupName);
            await Clients.Caller.SendAsync("GroupError", groupName, ex.Message);
        }
    }

    public async Task LeaveGroupAsync(string groupName)
    {
        try
        {
            var senderInfo = transport.GetOnlineClients().FirstOrDefault(c => c.SignalRConnectionId == Context.ConnectionId);
            var senderName = senderInfo?.ConnectionInfo.Name ?? "Unknown";

            var success = await transport.LeaveGroupAsync(groupName, senderName, Context.ConnectionId);

            if (success)
            {
                await Clients.Caller.SendAsync("GroupLeft", groupName);
                await Clients.Group(groupName).SendAsync("UserLeftGroup", senderName, groupName);

                logger.LogInformation("User {UserName} left group {GroupName}", senderName, groupName);
            }
            else
            {
                await Clients.Caller.SendAsync("GroupError", groupName, "Failed to leave group or not in group");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error leaving group {GroupName}", groupName);
            await Clients.Caller.SendAsync("GroupError", groupName, ex.Message);
        }
    }

    public async Task SendGroupMessageAsync(string groupName, string content)
    {
        try
        {
            var senderInfo = transport.GetOnlineClients().FirstOrDefault(c => c.SignalRConnectionId == Context.ConnectionId);
            var senderName = senderInfo?.ConnectionInfo.Name ?? "Unknown";

            var message = new Message
            {
                Content = content,
                Sender = senderName,
                Receiver = $"group:{groupName}",
                Type = MessageType.Text,
                Timestamp = DateTime.UtcNow
            };

            var success = await transport.SendGroupMessageAsync(groupName, message);

            if (success)
            {
                await Clients.Caller.SendAsync("GroupMessageSent", groupName, message.Id);
            }
            else
            {
                await Clients.Caller.SendAsync("GroupError", groupName, "Failed to send group message");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending group message to {GroupName}", groupName);
            await Clients.Caller.SendAsync("GroupError", groupName, ex.Message);
        }
    }

    public async Task GetGroupMembersAsync(string groupName)
    {
        try
        {
            var members = transport.GetGroupMembers(groupName);
            await Clients.Caller.SendAsync("GroupMembers", groupName, members);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting group members for {GroupName}", groupName);
            await Clients.Caller.SendAsync("GroupError", groupName, ex.Message);
        }
    }
    #endregion

    #region Information and Status
    public async Task GetOnlineUsersAsync()
    {
        try
        {
            var onlineUsers = transport.GetOnlineClients().Select(c => new
            {
                c.ConnectionInfo.Id,
                c.ConnectionInfo.Name,
                c.ConnectionInfo.ConnectedAt,
                Groups = transport.GetClientGroups(c.ConnectionInfo.Name)
            }).ToList();

            await Clients.Caller.SendAsync("OnlineUsersList", onlineUsers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting online users");
            await Clients.Caller.SendAsync("SystemError", "Failed to get online users");
        }
    }

    public async Task GetServerStatusAsync()
    {
        try
        {
            var stats = transport.GetStatistics();
            await Clients.Caller.SendAsync("ServerStatus", stats);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting server status");
            await Clients.Caller.SendAsync("SystemError", "Failed to get server status");
        }
    }

    public async Task PingAsync()
    {
        try
        {
            await Clients.Caller.SendAsync("Pong", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in PingAsync");
        }
    }
    #endregion

    #region Hub Events
    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("New connection: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            logger.LogInformation("Connection disconnected: {ConnectionId}, Exception: {Exception}",
                Context.ConnectionId, exception?.Message);

            transport.RemoveConnection(Context.ConnectionId);

            if (exception != null)
            {
                transport.OnError($"Disconnected with error: {exception.Message}", Context.ConnectionId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in OnDisconnectedAsync");
            transport.OnError($"Disconnect handling error: {ex.Message}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
    #endregion
}

/// <summary>
/// Enhanced SignalR client information container
/// </summary>
internal class EnhancedSignalRClientInfo(
    ConnectionInfo connectionInfo,
    string signalRConnectionId,
    HubCallerContext context)
{
    public ConnectionInfo ConnectionInfo { get; } = connectionInfo;
    public string SignalRConnectionId { get; } = signalRConnectionId;
    public HubCallerContext Context { get; } = context;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    public void UpdateLastActivity()
    {
        LastActivity = DateTime.UtcNow;
    }
}

/// <summary>
/// SignalR server statistics
/// </summary>
public class SignalRServerStatistics
{
    public int ActiveConnections { get; set; }
    public int ActiveGroups { get; set; }
    public long TotalMessages { get; set; }
    public TimeSpan Uptime { get; set; }
    public bool IsRunning { get; set; }
    public string HubPath { get; set; } = string.Empty;
    public string ServerAddress { get; set; } = string.Empty;
}