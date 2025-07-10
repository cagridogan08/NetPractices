using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Messaging.ModelLibrary.SignalR
{
    //TODO=> Implement SignalR client for messaging
    public class SignalRClient : IMessageClient
    {
        #region Fields

        private HubConnection? _hubConnection;

        private CancellationTokenSource? _cancellationTokenSource;

        private bool _disposed;

        public IList<(string, string)> OtherConnections { get; } = new List<(string, string)>();


        #endregion

        #region Events

        public event EventHandler<MessageEventArgs>? MessageReceived;
        public event EventHandler<ConnectionEventArgs>? Connected;
        public event EventHandler<ConnectionEventArgs>? Disconnected;
        public event EventHandler<ErrorEventArgs>? ErrorOccurred;

        #endregion

        #region Properties

        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
        public ConnectionInfo? ConnectionInfo { get; private set; }
        public TransportType TransportType => TransportType.SignalR;

        #endregion

        #region Methods

        public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
        {
            try
            {
                await DisconnectAsync(); // Ensure any previous connection is closed

                var serverUrl = configuration.GetValueOrDefault("ServerUrl") as string;
                if (string.IsNullOrEmpty(serverUrl))
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs("ServerUrl is required for SignalR connection"));
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
                      options.AccessTokenProvider = () => Task.FromResult(accessToken);
                  }

                  // Configure transport
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
                    builder.WithStatefulReconnect(); // Enable stateful reconnect for better error handling
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

                // Configure connection options
                _hubConnection.HandshakeTimeout = TimeSpan.FromSeconds(handshakeTimeout);
                _hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(keepAliveInterval);
                _hubConnection.ServerTimeout = TimeSpan.FromSeconds(serverTimeout);
                _cancellationTokenSource = new();
                // Set up event handlers
                SetupEventHandlers();

                // Connect to the hub
                if (_cancellationTokenSource != null) await _hubConnection.StartAsync(_cancellationTokenSource.Token);

                // Send connection handshake
                await _hubConnection.InvokeAsync("JoinAsync", clientName);

                ConnectionInfo = new ConnectionInfo
                {
                    Id = _hubConnection.ConnectionId ?? Guid.NewGuid().ToString(),
                    Name = clientName,
                    Address = serverUrl,
                    Properties = new Dictionary<string, object>
                    {
                        ["State"] = _hubConnection.State.ToString()
                    }
                };

                Connected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                return true;
            }
            catch (Exception e)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs("Failed to connect to SignalR server.", e, ConnectionInfo));

                return false;
            }
        }

        private void SetupEventHandlers()
        {
            if (_hubConnection == null) return;

            // Handle received messages
            _hubConnection.On<Message>("ReceiveMessage", (message) =>
            {
                if (ConnectionInfo != null)
                {
                    MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
                }
            });

            // Handle connection events
            _hubConnection.Closed += (error) =>
            {
                if (ConnectionInfo != null && !_disposed)
                {
                    if (error != null)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorEventArgs($"SignalR connection closed: {error.Message}", error, ConnectionInfo));
                    }
                    Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                }
                return Task.CompletedTask;
            };

            _hubConnection.Reconnecting += (error) =>
            {
                if (error != null)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"SignalR reconnecting: {error.Message}", error, ConnectionInfo));
                }
                return Task.CompletedTask;
            };

            _hubConnection.Reconnected += (connectionId) =>
            {
                if (ConnectionInfo != null)
                {
                    ConnectionInfo.Id = connectionId ?? ConnectionInfo.Id;
                    ConnectionInfo.Properties["State"] = _hubConnection.State.ToString();
                    Connected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                }
                return Task.CompletedTask;
            };

            // Handle user connection notifications
            _hubConnection.On<string, string>("UserConnected", (connectionId, userName) =>
            {
                OtherConnections.Add((connectionId, userName));
                // Optional: Handle other users connecting
            });

            _hubConnection.On<string, string>("UserDisconnected", (connectionId, userName) =>
            {
                var item = OtherConnections.FirstOrDefault(c => c.Item1 == connectionId);
                if (item != default)
                {
                    OtherConnections.Remove(item);
                }
                // Optional: Handle other users disconnecting
            });

            // Handle errors
            _hubConnection.On<string>("Error", (errorMessage) =>
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"SignalR hub error: {errorMessage}", null, ConnectionInfo));
            });
        }

        public async Task DisconnectAsync()
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
                        catch (Exception)
                        {
                            // Ignore errors during leave
                        }
                    }

                    await _hubConnection.DisposeAsync();
                    _hubConnection = null;
                }

                if (ConnectionInfo != null)
                {
                    Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                    ConnectionInfo = null;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"SignalR disconnection error: {ex.Message}", ex));
            }
        }

        public async Task<bool> SendMessageAsync(Message message)
        {
            try
            {
                if (!IsConnected || _hubConnection == null || _disposed)
                    return false;
                await _hubConnection.InvokeAsync("SendMessageAsync", message);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"SignalR send error: {ex.Message}", ex, ConnectionInfo));
                return false;
            }

        }

        public void Dispose()
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
}
