using System.IO.Pipes;
using System.Text.Json;

namespace Messaging.ModelLibrary.Pipe;

public class NamedPipeClient : IMessageClient
{
    private NamedPipeClientStream _pipeClient;
    private Task _readTask;
    private CancellationTokenSource _cancellationTokenSource;
    private bool _disposed;

    public event EventHandler<MessageEventArgs> MessageReceived;
    public event EventHandler<ConnectionEventArgs> Connected;
    public event EventHandler<ConnectionEventArgs> Disconnected;
    public event EventHandler<ErrorEventArgs> ErrorOccurred;

    public bool IsConnected => _pipeClient?.IsConnected == true;
    public ConnectionInfo ConnectionInfo { get; private set; }
    public TransportType TransportType => TransportType.NamedPipe;

    public async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            var serverName = configuration?.GetValueOrDefault("ServerName", ".") as string ?? ".";
            var pipeName = configuration?.GetValueOrDefault("PipeName", "GenericMessagingApp") as string ?? "GenericMessagingApp";
            var timeout = configuration?.GetValueOrDefault("Timeout", 5000) as int? ?? 5000;
            var clientName = configuration?.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName;

            _cancellationTokenSource = new CancellationTokenSource();
            _pipeClient = new NamedPipeClientStream(
                serverName,
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await _pipeClient.ConnectAsync(timeout);

            ConnectionInfo = new ConnectionInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = clientName,
                Address = $"{serverName}\\{pipeName}"
            };
            _readTask = Task.Run(ReadMessagesAsync, _cancellationTokenSource.Token);

            Connected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Connection failed: {ex.Message}", ex));
            return false;
        }
    }




    public async Task DisconnectAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_readTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_readTask, Task.Delay(2000)) == _readTask)
                    {
                        await _readTask;
                    }
                }
                catch (Exception)
                {
                    // Ignore exceptions during task wait
                }
                _readTask = null;
            }

            try
            {
                _pipeClient?.Close();
            }
            catch (Exception)
            {
                // Ignore close errors
            }

            _pipeClient?.Dispose();
            _pipeClient = null;

            if (ConnectionInfo != null)
            {
                Disconnected?.Invoke(this, new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Disconnection error: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _disposed) return false;

            var json = JsonSerializer.Serialize(message);
            using var writer = new StreamWriter(_pipeClient, leaveOpen: true);
            await writer.WriteLineAsync(json);
            await writer.FlushAsync();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    private async Task ReadMessagesAsync()
    {
        try
        {
            using var reader = new StreamReader(_pipeClient, leaveOpen: true);

            while (!_cancellationTokenSource.Token.IsCancellationRequested &&
                   !_disposed &&
                   _pipeClient.IsConnected &&
                   await reader.ReadLineAsync() is { } json)
            {
                try
                {
                    var message = JsonSerializer.Deserialize<Message>(json);
                    MessageReceived?.Invoke(this, new MessageEventArgs(message, ConnectionInfo));
                }
                catch (JsonException ex)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex, ConnectionInfo));
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Pipe was disposed - this is expected during shutdown
        }
        catch (InvalidOperationException)
        {
            // Pipe is closed - this is expected during disconnect
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested - this is expected during shutdown
        }
        catch (Exception ex)
        {
            if (!_cancellationTokenSource.Token.IsCancellationRequested && !_disposed)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Read error: {ex.Message}", ex, ConnectionInfo));
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
                DisconnectAsync().Wait(2000); // Wait up to 2 seconds
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }
    }
}