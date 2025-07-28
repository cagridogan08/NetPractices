using System.Text.Json;
using Messaging.ModelLibrary.Abstract;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Messaging.ModelLibrary.SignalR;

public sealed class SignalRClient : MessageClientBase
{
    #region Fields

    private HubConnection? _hubConnection;

    private CancellationTokenSource? _cancellationTokenSource;


    public IList<(string, string)> OtherConnections { get; } = new List<(string, string)>();


    #endregion

    #region Properties

    public override bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.SignalR;

    #endregion

    #region Methods

    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var serverUrl = configuration.GetValueOrDefault("ServerUrl") as string;
            if (string.IsNullOrEmpty(serverUrl))
            {
                OnErrorOccurred(new ErrorEventArgs("ServerUrl is required for SignalR connection"));
                return false;
            }

            var clientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;
            var accessToken = configuration.GetValueOrDefault("AccessToken") as string;
            var enableDetailedErrors = configuration.GetValueOrDefault("EnableDetailedErrors", false) as bool? ?? false;
            var handshakeTimeout = configuration.GetValueOrDefault("HandshakeTimeout", 15) as int? ?? 15;
            var keepAliveInterval = configuration.GetValueOrDefault("KeepAliveInterval", 15) as int? ?? 15;
            var serverTimeout = configuration.GetValueOrDefault("ServerTimeout", 30) as int? ?? 30;
            var enableAutoReconnect = configuration.GetValueOrDefault("EnableAutoReconnect", true) as bool? ?? true;
            var transport = configuration.GetValueOrDefault("Transport", "WebSockets") as string ?? "WebSockets";

            var builder = new HubConnectionBuilder().WithUrl(serverUrl, options =>
            {
                if (!string.IsNullOrEmpty(accessToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult(accessToken)!;
                }

                options.Transports = transport.ToLower() switch
                {
                    "websockets" => Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets,
                    "serversent" or "serversentevents" => Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents,
                    "longpolling" => Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling,
                    _ => Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets
                };
            })
            .ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

            if (enableDetailedErrors)
            {
                builder.WithStatefulReconnect();
            }

            if (enableAutoReconnect)
            {
                builder.WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                });
            }

            _hubConnection = builder.Build();

            _hubConnection.HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeout);
            _hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveInterval);
            _hubConnection.ServerTimeout = TimeSpan.FromSeconds(serverTimeout);
            _cancellationTokenSource = new();

            SetupEventHandlers();

            if (_cancellationTokenSource != null)
                await _hubConnection.StartAsync(_cancellationTokenSource.Token);

            await _hubConnection.InvokeAsync("JoinAsync", clientName);

            ConnectionInfo = new ConnectionInfo
            {
                Id = clientName, // Use client name for easier routing
                Name = clientName,
                Address = serverUrl,
                ConnectedAt = DateTime.UtcNow,
                IsActive = true,
                Properties = new Dictionary<string, object>
                {
                    ["State"] = _hubConnection.State.ToString(),
                    ["ConnectionId"] = _hubConnection.ConnectionId ?? "Unknown"
                }
            };

            OnConnected(new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs("Failed to connect to SignalR server.", e, ConnectionInfo));
            return false;
        }
    }

    private void SetupEventHandlers()
    {
        if (_hubConnection == null) return;

        _hubConnection.On<Message>("ReceiveMessage", (message) =>
        {
            if (ConnectionInfo != null)
            {
                OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
            }
        });

        _hubConnection.Closed += (error) =>
        {
            if (ConnectionInfo != null && !_disposed)
            {
                if (error != null)
                {
                    OnErrorOccurred(new ErrorEventArgs($"SignalR connection closed: {error.Message}", error, ConnectionInfo));
                }
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
            }
            return Task.CompletedTask;
        };

        _hubConnection.Reconnecting += (error) =>
        {
            if (error != null)
            {
                OnErrorOccurred(new ErrorEventArgs($"SignalR reconnecting: {error.Message}", error, ConnectionInfo));
            }
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += (connectionId) =>
        {
            if (ConnectionInfo != null)
            {
                ConnectionInfo.Properties["ConnectionId"] = connectionId ?? "Unknown";
                ConnectionInfo.Properties["State"] = _hubConnection.State.ToString();
                OnConnected(new ConnectionEventArgs(ConnectionInfo));
            }
            return Task.CompletedTask;
        };

        _hubConnection.On<string, string>("UserConnected", (connectionId, userName) =>
        {
            OtherConnections.Add((connectionId, userName));
            var clientInfo = new ClientInfo
            {
                Id = userName,
                Name = userName,
                DisplayName = userName,
                IsOnline = true,
                LastSeen = DateTime.UtcNow,
                TransportType = TransportType.SignalR
            };
            _knownClients[userName] = clientInfo;
            OnClientDiscovered(new ClientDiscoveryEventArgs(clientInfo, true));
        });

        _hubConnection.On<string, string>("UserDisconnected", (connectionId, userName) =>
        {
            var item = OtherConnections.FirstOrDefault(c => c.Item1 == connectionId);
            if (item != default)
            {
                OtherConnections.Remove(item);
            }

            if (_knownClients.TryGetValue(userName, out var client))
            {
                client.IsOnline = false;
                OnClientDisconnected(new ClientDiscoveryEventArgs(client, false));
            }
        });

        _hubConnection.On<string>("Error", (errorMessage) =>
        {
            OnErrorOccurred(new ErrorEventArgs($"SignalR hub error: {errorMessage}", null, ConnectionInfo));
        });
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            if (_hubConnection != null)
            {
                if (ConnectionInfo != null && _hubConnection.State == HubConnectionState.Connected)
                {
                    try
                    {
                        await _hubConnection.InvokeAsync("LeaveAsync", ConnectionInfo.Name);
                    }
                    catch
                    {
                        /*ignored*/
                    }
                }

                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }

            if (ConnectionInfo != null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"SignalR disconnection error: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _hubConnection == null || _disposed)
                return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            await _hubConnection.InvokeAsync("SendMessageAsync", message);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"SignalR send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }

    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            try
            {
                DisconnectAsync().Wait(5000);
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }
    }

    #endregion

}