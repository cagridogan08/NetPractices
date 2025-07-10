using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.SignalR;

public class SignalRTransport(string address = "localhost", int port = 5003, string path = "/messagingHub")
    : IMessageTransport
{
    #region Fields

    private readonly ConcurrentDictionary<string, SignalRClientInfo> _connections = new();
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
    public TransportType TransportType => TransportType.SignalR;
    public IReadOnlyList<ConnectionInfo> Connections => _connections.Values.Select(c => c.ConnectionInfo).ToList();

    #endregion

    #region Methods

    /// <summary>
    /// Starts the SignalR server with the provided configuration.
    /// 
    /// Configuration dictionary keys:
    /// - "Address" (string, optional): Server bind address. Defaults to constructor value.
    /// - "Port" (int, optional): Server port. Defaults to constructor value.
    /// - "HubPath" (string, optional): Hub endpoint path. Defaults to constructor value.
    /// - "EnableHttps" (bool, optional): Whether to use HTTPS. Defaults to false.
    /// - "CertificatePath" (string, optional): Path to SSL certificate for HTTPS.
    /// - "CertificatePassword" (string, optional): Password for SSL certificate.
    /// - "EnableCors" (bool, optional): Enable CORS for cross-origin requests. Defaults to true.
    /// - "CorsOrigins" (string[], optional): Allowed CORS origins. Defaults to ["*"].
    /// - "EnableDetailedErrors" (bool, optional): Enable detailed error messages. Defaults to false.
    /// - "MaxBufferSize" (int, optional): Maximum message buffer size. Defaults to 32KB.
    /// - "EnableMessagePack" (bool, optional): Enable MessagePack protocol. Defaults to false.
    /// </summary>
    public async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
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
            _cancellationTokenSource = new CancellationTokenSource();

            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        // Add SignalR services
                        services.AddSignalR(options =>
                        {
                            options.EnableDetailedErrors = enableDetailedErrors;
                            options.MaximumReceiveMessageSize = maxBufferSize;
                            options.StreamBufferCapacity = 10;
                        });
                        // Add CORS if enabled
                        if (enableCors)
                        {
                            services.AddCors(options =>
                            {
                                options.AddPolicy("CorsPolicy", policy =>
                                {
                                    policy.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials();
                                });
                            });
                        }

                        // Register the transport instance
                        services.AddSingleton(this);
                    });

                    webBuilder.Configure(app =>
                    {
                        if (enableCors)
                        {
                            app.UseCors();
                        }

                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHub<MessagingHub>(hubPath);
                        });
                    });

                    var url = enableHttps ? $"https://{address1}:{port1}" : $"http://{address1}:{port1}";
                    webBuilder.UseUrls(url);

                    if (enableHttps && !string.IsNullOrEmpty(certPath))
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.ConfigureHttpsDefaults(httpsOptions =>
                            {
                                httpsOptions.ServerCertificate = !string.IsNullOrEmpty(certPassword) ? new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath, certPassword) : new System.Security.Cryptography.X509Certificates.X509Certificate2(certPath);
                            });
                        });
                    }
                })
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Information);
                });

            _host = builder.Build();
            await _host.StartAsync(_cancellationTokenSource.Token);

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start SignalR server: {ex.Message}", ex));
            return false;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            // Notify all clients of disconnection
            foreach (var client in _connections.Values)
            {
                ClientDisconnected?.Invoke(this, new ConnectionEventArgs(client.ConnectionInfo));
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
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Error stopping SignalR server: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (!IsRunning || _host == null)
                return false;

            var hubContext = _host.Services.GetRequiredService<IHubContext<MessagingHub>>();

            if (string.IsNullOrEmpty(connectionId))
            {
                return await BroadcastMessageAsync(message);
            }

            if (_connections.ContainsKey(connectionId))
            {
                await hubContext.Clients.Client(connectionId).SendAsync("ReceiveMessage", message);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send SignalR message: {ex.Message}", ex));
            return false;
        }
    }

    public async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            if (!IsRunning || _host == null)
                return false;

            var hubContext = _host.Services.GetRequiredService<IHubContext<MessagingHub>>();
            await hubContext.Clients.All.SendAsync("ReceiveMessage", message);
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to broadcast SignalR message: {ex.Message}", ex));
            return false;
        }
    }

    internal void AddConnection(string connectionId, string userName)
    {
        var connectionInfo = new ConnectionInfo
        {
            Id = connectionId,
            Name = userName,
            Address = $"{address}:{port}{path}",
            Properties = new Dictionary<string, object>
            {
                ["Transport"] = "SignalR",
                ["HubPath"] = path
            }
        };

        var clientInfo = new SignalRClientInfo(connectionInfo);
        _connections.TryAdd(connectionId, clientInfo);
        ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
    }

    internal void RemoveConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var clientInfo))
        {
            ClientDisconnected?.Invoke(this, new ConnectionEventArgs(clientInfo.ConnectionInfo));
        }
    }

    internal void OnMessageReceived(Message message, string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var clientInfo))
        {
            clientInfo.UpdateLastActivity();
            MessageReceived?.Invoke(this, new MessageEventArgs(message, clientInfo.ConnectionInfo));
        }
    }

    internal void OnError(string error, string connectionId)
    {
        var clientInfo = _connections.TryGetValue(connectionId, out var client) ? client : null;
        ErrorOccurred?.Invoke(this, new ErrorEventArgs(error, null, clientInfo?.ConnectionInfo));
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

#region SignalR Hub Implementation

public class MessagingHub(SignalRTransport transport) : Hub
{
    public async Task JoinAsync(string userName)
    {
        try
        {
            transport.AddConnection(Context.ConnectionId, userName);

            // Notify other clients
            await Clients.Others.SendAsync("UserConnected", Context.ConnectionId, userName);
        }
        catch (Exception ex)
        {
            transport.OnError($"Join error: {ex.Message}", Context.ConnectionId);
        }
    }

    public async Task LeaveAsync(string userName)
    {
        try
        {
            transport.RemoveConnection(Context.ConnectionId);

            // Notify other clients
            await Clients.Others.SendAsync("UserDisconnected", Context.ConnectionId, userName);
        }
        catch (Exception ex)
        {
            transport.OnError($"Leave error: {ex.Message}", Context.ConnectionId);
        }
    }

    public async Task SendMessageAsync(Message message)
    {
        try
        {
            // Update message sender if not set
            if (string.IsNullOrEmpty(message.Sender))
            {
                message.Sender = Context.UserIdentifier ?? Context.ConnectionId;
            }

            // Handle message routing
            if (string.IsNullOrEmpty(message.Receiver))
            {
                // Broadcast to all clients
                transport.OnMessageReceived(message, Context.ConnectionId);
            }
            else
            {
                // Send to specific receiver
                await Clients.User(message.Receiver).SendAsync("ReceiveMessage", message);
            }

            // Notify the transport
            //transport.OnMessageReceived(message, Context.ConnectionId);
        }
        catch (Exception ex)
        {
            transport.OnError($"Send message error: {ex.Message}", Context.ConnectionId);
        }
    }


    public async Task SendMessageToGroupAsync(string groupName, Message message)
    {
        try
        {
            if (string.IsNullOrEmpty(message.Sender))
            {
                message.Sender = Context.UserIdentifier ?? Context.ConnectionId;
            }

            await Clients.Group(groupName).SendAsync("ReceiveMessage", message);
            transport.OnMessageReceived(message, Context.ConnectionId);
        }
        catch (Exception ex)
        {
            transport.OnError($"Send to group error: {ex.Message}", Context.ConnectionId);
        }
    }

    public async Task JoinGroupAsync(string groupName)
    {
        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            await Clients.Group(groupName).SendAsync("UserJoinedGroup", Context.ConnectionId, groupName);
        }
        catch (Exception ex)
        {
            transport.OnError($"Join group error: {ex.Message}", Context.ConnectionId);
        }
    }

    public async Task LeaveGroupAsync(string groupName)
    {
        try
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            await Clients.Group(groupName).SendAsync("UserLeftGroup", Context.ConnectionId, groupName);
        }
        catch (Exception ex)
        {
            transport.OnError($"Leave group error: {ex.Message}", Context.ConnectionId);
        }
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            transport.RemoveConnection(Context.ConnectionId);

            if (exception != null)
            {
                transport.OnError($"Disconnected with error: {exception.Message}", Context.ConnectionId);
            }
        }
        catch (Exception ex)
        {
            transport.OnError($"Disconnect handling error: {ex.Message}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

#endregion

#region Helper Classes

internal class SignalRClientInfo(ConnectionInfo connectionInfo)
{
    public ConnectionInfo ConnectionInfo { get; } = connectionInfo;
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    public void UpdateLastActivity()
    {
        LastActivity = DateTime.UtcNow;
    }
}

#endregion