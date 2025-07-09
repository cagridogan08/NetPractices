using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;

namespace Messaging.ModelLibrary.Pipe;

public class NamedPipeTransport : IMessageTransport
{
    private readonly string _pipeName;
    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
    private CancellationTokenSource _cancellationTokenSource;
    private Task _serverTask;
    private bool _disposed;

    public NamedPipeTransport(string pipeName = "GenericMessagingApp")
    {
        _pipeName = pipeName;
    }

    public event EventHandler<MessageEventArgs> MessageReceived;
    public event EventHandler<ConnectionEventArgs> ClientConnected;
    public event EventHandler<ConnectionEventArgs> ClientDisconnected;
    public event EventHandler<ErrorEventArgs> ErrorOccurred;

    public bool IsRunning { get; private set; }
    public TransportType TransportType => TransportType.NamedPipe;
    public IReadOnlyList<ConnectionInfo> Connections => _connections.Values.Select(c => c.Info).ToList();

    public async Task<bool> StartAsync(Dictionary<string, object> configuration = null)
    {
        try
        {
            await StopAsync();

            _cancellationTokenSource = new CancellationTokenSource();
            _serverTask = Task.Run(RunServerAsync, _cancellationTokenSource.Token);

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start named pipe server: {ex.Message}", ex));
            return false;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_serverTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_serverTask, Task.Delay(3000)) == _serverTask)
                    {
                        await _serverTask;
                    }
                }
                catch (Exception)
                {
                    // Ignore server task exceptions during shutdown
                }
                _serverTask = null;
            }

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
            IsRunning = false;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Error stopping server: {ex.Message}", ex));
        }
    }

    public async Task<bool> SendMessageAsync(Message message, string connectionId = null)
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
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to send message: {ex.Message}", ex));
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
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to broadcast message: {ex.Message}", ex));
            return false;
        }
    }

    private async Task RunServerAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);

                var connectionInfo = new ConnectionInfo
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Unknown",
                    Address = _pipeName
                };

                var clientConnection = new ClientConnection(pipeServer, connectionInfo, _cancellationTokenSource.Token);
                clientConnection.MessageReceived += OnClientMessageReceived;
                clientConnection.Disconnected += OnClientDisconnected;
                clientConnection.ErrorOccurred += OnClientErrorOccurred;

                _connections.TryAdd(connectionInfo.Id, clientConnection);
                ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));

                clientConnection.StartReading();

                // Don't dispose the pipe here - let ClientConnection manage it
                pipeServer = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Server error: {ex.Message}", ex));
                pipeServer?.Dispose();
            }
        }
    }

    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        // Update connection name if this is the first message
        if (sender is ClientConnection connection && connection.Info.Name == "Unknown")
        {
            connection.Info.Name = e.Message.Sender;
        }

        MessageReceived?.Invoke(this, e);
    }

    private void OnClientDisconnected(object? sender, ConnectionEventArgs e)
    {
        _connections.TryRemove(e.Connection.Id, out _);
        ClientDisconnected?.Invoke(this, e);
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
                StopAsync().Wait(3000); // Wait up to 3 seconds
            }
            catch (Exception)
            {
                // Ignore disposal errors
            }
        }
    }

    private class ClientConnection : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly CancellationToken _cancellationToken;
        private Task _readTask;
        private bool _disposed;

        public ClientConnection(NamedPipeServerStream pipe, ConnectionInfo info, CancellationToken cancellationToken)
        {
            _pipe = pipe;
            Info = info;
            _cancellationToken = cancellationToken;
        }

        public ConnectionInfo Info { get; }

        public event EventHandler<MessageEventArgs> MessageReceived;
        public event EventHandler<ConnectionEventArgs> Disconnected;
        public event EventHandler<ErrorEventArgs> ErrorOccurred;

        public void StartReading()
        {
            _readTask = Task.Run(ReadMessagesAsync, _cancellationToken);
        }

        public async Task<bool> SendMessageAsync(Message message)
        {
            try
            {
                if (_disposed || !_pipe.IsConnected)
                    return false;

                var json = JsonSerializer.Serialize(message);
                using var writer = new StreamWriter(_pipe, leaveOpen: true);
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
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex, Info));
                return false;
            }
        }

        private async Task ReadMessagesAsync()
        {
            try
            {
                using var reader = new StreamReader(_pipe, leaveOpen: true);
                string json;

                while (!_cancellationToken.IsCancellationRequested &&
                       !_disposed &&
                       _pipe.IsConnected &&
                       (json = await reader.ReadLineAsync()) != null)
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<Message>(json);
                        MessageReceived?.Invoke(this, new MessageEventArgs(message, Info));
                    }
                    catch (JsonException ex)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex, Info));
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
                if (!_cancellationToken.IsCancellationRequested && !_disposed)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Read error: {ex.Message}", ex, Info));
                }
            }
            finally
            {
                if (!_disposed)
                {
                    Disconnected?.Invoke(this, new ConnectionEventArgs(Info));
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
                    _pipe?.Close();
                    _pipe?.Dispose();
                }
                catch (Exception)
                {
                    // Ignore disposal errors
                }

                try
                {
                    _readTask?.Wait(1000); // Wait up to 1 second for read task to complete
                }
                catch (Exception)
                {
                    // Ignore task wait errors
                }
            }
        }
    }
}