using Messaging.ModelLibrary.Abstract;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Messaging.ModelLibrary.ServerSentEvents;

public class ServerSentEventTransport : MessageTransportBase
{
    #region Fields

    private new readonly ConcurrentDictionary<string, SseClientConnection> _connections = new();
    private IHost? _host;
    private CancellationTokenSource? _cancellationTokenSource;
    private SseConfig? _config;

    #endregion

    #region Properties

    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.ServerSentEvents;

    #endregion

    #region Methods

    /// <summary>
    /// Starts the server-sent events transport using the provided configuration.
    /// Stops any previous instance, then initializes required connection properties such as host, port,
    /// and endpoint paths for events and messages.
    /// </summary>
    /// <param name="configuration">
    /// A dictionary containing optional keys:
    /// "Host" (string, default "localhost"), 
    /// "Port" (int, default 8080), 
    /// "EventsPath" (string, default "/events"), 
    /// "MessagesPath" (string, default "/messages"), 
    /// "ClientName" (string, default is current environment user).
    /// </param>
    /// <returns>
    /// A task that resolves to true if the transport starts successfully; otherwise, false.
    /// </returns>
    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();
            _config = SseConfig.FromDictionary(configuration);

            _cancellationTokenSource = new CancellationTokenSource();
            var builder = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton(this);
                        services.AddCors(options =>
                        {
                            options.AddDefaultPolicy(policy =>
                            {
                                policy.AllowAnyOrigin()
                                    .AllowAnyMethod()
                                    .AllowAnyHeader();
                            });
                        });
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseCors();
                        app.UseRouting();

                        app.UseEndpoints(endpoints =>
                        {
                            // SSE endpoint
                            endpoints.MapGet(_config.EventsPath, HandleSseConnection);

                            // Message posting endpoint (for client-to-server messages)
                            endpoints.MapPost(_config.MessagesPath, HandleMessagePost);

                            // Health check
                            endpoints.MapGet("/health",
                                async context => { await context.Response.WriteAsync("SSE server running"); });
                        });
                    });
                    webBuilder.UseUrls($"http://{_config.Host}:{_config.Port}");
                });

            _host = builder.Build();
            await _host.StartAsync(_cancellationTokenSource.Token);
            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to start SSE transport: {ex.Message}", ex));
            return false;
        }
    }
    protected override string GetClientAddress(object? transportSpecificData)
    {
        return transportSpecificData is HttpContext context
            ? context.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
            : "Unknown";
    }

    #region EventMethods

    private async Task HandleSseConnection(HttpContext context)
    {
        try
        {
            var clientName = context.Request.Query["client"].FirstOrDefault() ??
                           context.Connection.Id;

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.Add("Cache-Control", "no-cache");
            context.Response.Headers.Add("Connection", "keep-alive");
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");

            var connectionInfo = new ConnectionInfo
            {
                Id = clientName,
                Name = clientName,
                Address = GetClientAddress(context),
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };

            var connection = new SseClientConnection(clientName, context, connectionInfo);
            _connections[clientName] = connection;

            RegisterClient(clientName, clientName, context);

            // Send welcome message
            var welcomeMessage = new Message
            {
                Content = $"Connected to SSE server as {clientName}",
                Sender = "System",
                Receiver = clientName,
                Type = MessageType.System
            };
            await connection.SendMessageAsync(welcomeMessage);

            // Keep connection alive until cancelled
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // Expected when connection is closed
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"SSE connection error: {ex.Message}", ex));
        }
        finally
        {
            var clientName = context.Request.Query["client"].FirstOrDefault() ?? context.Connection.Id;
            if (!string.IsNullOrEmpty(clientName) && _connections.TryRemove(clientName, out var connection))
            {
                UnregisterClient(clientName);
                await connection.CloseAsync();
            }
        }
    }

    private async Task HandleMessagePost(HttpContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var json = await reader.ReadToEndAsync();
            var message = JsonSerializer.Deserialize<Message>(json);

            if (message != null)
            {
                // Handle client registration
                if (message is { Type: MessageType.System, Content: "CLIENT_REGISTER" })
                {
                    // Registration is handled in SSE connection
                    context.Response.StatusCode = 200;
                    return;
                }

                HandleReceivedMessage(message, message.Sender);
                context.Response.StatusCode = 200;
                await context.Response.WriteAsync("Message received");
            }
            else
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Invalid message format");
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling message post: {ex.Message}", ex));
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Internal server error");
        }
    }

    #endregion


    public override async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            foreach (var connection in _connections.Values)
            {
                await connection.CloseAsync();
            }

            _connections.Clear();

            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(10));
                _host.Dispose();
                _host = null;
            }

            IsRunning = false;
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to stop SSE Transpors:{e.Message}", e));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (!IsRunning) return false;
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
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send message via SSE: {e.Message}", e));
            return false;
        }
    }

    public override async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            var tasks = _connections.Values.Select(conn => conn.SendMessageAsync(message));
            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast SSE message: {ex.Message}", ex));
            return false;
        }
    }

    #endregion


    private class SseClientConnection(string clientName, HttpContext context, ConnectionInfo connectionInfo)
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public string ClientName { get; } = clientName;
        public ConnectionInfo ConnectionInfo { get; } = connectionInfo;

        public async Task<bool> SendMessageAsync(Message message)
        {
            try
            {
                await _writeLock.WaitAsync();

                var json = JsonSerializer.Serialize(message);
                var data = $"data: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(data);

                await context.Response.Body.WriteAsync(bytes);
                await context.Response.Body.FlushAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                await context.Response.Body.FlushAsync();
                context.Abort();
            }
            catch
            {
                /* ignore */
            }
        }
    }
}






