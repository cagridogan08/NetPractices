using System.IO.Pipes;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Pipe;
public class NamedPipeClient : MessageClientBase
{
    #region Fields
    private NamedPipeClientStream? _pipeClient;
    private Task? _readTask;
    private CancellationTokenSource? _cancellationTokenSource;
    #endregion

    #region Properties
    public override bool IsConnected => _pipeClient?.IsConnected == true;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.NamedPipe;
    #endregion

    public override async Task<bool> ConnectAsync(Dictionary<string, object>? configuration)
    {
        try
        {
            await DisconnectAsync();

            var serverName = configuration?.GetValueOrDefault("ServerName", ".") as string ?? ".";
            var pipeName = configuration?.GetValueOrDefault("PipeName", "GenericMessagingApp") as string ?? "GenericMessagingApp";
            var timeout = configuration?.GetValueOrDefault("Timeout", 5000) as int? ?? 5000;
            var clientName = configuration?.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;

            _cancellationTokenSource = new CancellationTokenSource();
            _pipeClient = new NamedPipeClientStream(serverName, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await _pipeClient.ConnectAsync(timeout);

            ConnectionInfo = new ConnectionInfo
            {
                Id = clientName, // Use client name as ID for easier routing
                Name = clientName,
                Address = $"{serverName}\\{pipeName}",
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };

            _readTask = Task.Run(ReadMessagesAsync, _cancellationTokenSource.Token);

            // Send registration message
            var registrationMessage = new Message
            {
                Content = "CLIENT_REGISTER",
                Sender = clientName,
                Receiver = "System",
                Type = MessageType.System
            };
            await SendMessageAsync(registrationMessage);

            OnConnected(new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Connection failed: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            // Send unregister message
            if (IsConnected && ConnectionInfo != null)
            {
                var unregisterMessage = new Message
                {
                    Content = "CLIENT_UNREGISTER",
                    Sender = ConnectionInfo.Name,
                    Receiver = "System",
                    Type = MessageType.System
                };
                await SendMessageAsync(unregisterMessage);
            }

            _cancellationTokenSource?.Cancel();

            if (_readTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_readTask, Task.Delay(2000)) == _readTask)
                        await _readTask;
                }
                catch
                {
                    /*ignore*/
                }
                _readTask = null;
            }

            try
            {
                _pipeClient?.Close();
            }
            catch
            {
                /*ignore*/
            }

            _pipeClient?.Dispose();
            _pipeClient = null;

            if (ConnectionInfo != null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Disconnection error: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _disposed) return false;

            // Ensure sender is set
            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var json = JsonSerializer.Serialize(message);
            if (_pipeClient != null)
            {
                await using var writer = new StreamWriter(_pipeClient, leaveOpen: true);
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();
            }

            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    private async Task ReadMessagesAsync()
    {
        try
        {
            if (_pipeClient != null)
            {
                using var reader = new StreamReader(_pipeClient, leaveOpen: true);

                while (_cancellationTokenSource is { Token.IsCancellationRequested: false } &&
                       !_disposed &&
                       _pipeClient.IsConnected &&
                       await reader.ReadLineAsync() is { } json)
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<Message>(json);
                        if (message != null && ConnectionInfo is not null)
                            OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
                    }
                    catch (JsonException ex)
                    {
                        OnErrorOccurred(new ErrorEventArgs($"Message parse error: {ex.Message}", ex, ConnectionInfo));
                    }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_cancellationTokenSource is { Token.IsCancellationRequested: false } && !_disposed)
            {
                OnErrorOccurred(new ErrorEventArgs($"Read error: {ex.Message}", ex, ConnectionInfo));
            }
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            base.Dispose();
            try
            {
                DisconnectAsync().Wait(2000);
            }
            catch
            {
                /*ignore*/
            }
        }
    }
}