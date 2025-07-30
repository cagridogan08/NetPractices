using System.IO.Pipes;
using System.Text.Json;
using Messaging.ModelLibrary.Abstract;

namespace Messaging.ModelLibrary.Pipe;

public class NamedPipeTransport(string pipeName = "GenericMessagingApp") : MessageTransportBase
{
    #region Fields

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _serverTask;

    #endregion

    #region Properties
    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.NamedPipe;
    #endregion

    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
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
            OnErrorOccurred(new ErrorEventArgs($"Failed to start named pipe server: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_serverTask != null)
            {
                try
                {
                    if (await Task.WhenAny(_serverTask, Task.Delay(3000)) == _serverTask)
                        await _serverTask;
                }
                catch { /*ignored*/}
                _serverTask = null;
            }

            // Dispose all connections
            var connectionTasks = _connections.Values.Select(connection =>
                Task.Run(() => (connection.TransportData as PipeClientConnection)?.Dispose()));

            try
            {
                await Task.WhenAll(connectionTasks);
            }
            catch { /*ignored*/ }

            _connections.Clear();
            IsRunning = false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error stopping server: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (string.IsNullOrEmpty(connectionId))
                return await BroadcastMessageAsync(message);

            if (_connections.TryGetValue(connectionId, out var clientInfo) &&
                clientInfo.TransportData is PipeClientConnection connection)
            {
                return await connection.SendMessageAsync(message);
            }

            return false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send message: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            var tasks = _connections.Values
                .Where(c => c.TransportData is PipeClientConnection)
                .Select(c => ((PipeClientConnection)c.TransportData).SendMessageAsync(message));

            var results = await Task.WhenAll(tasks);
            return results.Any(r => r);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast message: {ex.Message}", ex));
            return false;
        }
    }

    protected override string GetClientAddress(object transportSpecificData)
    {
        return pipeName;
    }

    private async Task RunServerAsync()
    {
        while (_cancellationTokenSource is { Token.IsCancellationRequested: false })
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(_cancellationTokenSource.Token);

                var clientConnection = new PipeClientConnection(pipeServer, pipeName, _cancellationTokenSource.Token);
                clientConnection.MessageReceived += OnClientMessageReceived;
                clientConnection.Disconnected += OnClientDisconnected;
                clientConnection.ErrorOccurred += OnClientErrorOccurred;

                clientConnection.StartReading();

                // Don't dispose the pipe here - let ClientConnection manage it
                pipeServer = null;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                OnErrorOccurred(new ErrorEventArgs($"Server error: {ex.Message}", ex));
                pipeServer?.Dispose();
            }
        }
    }

    private void OnClientMessageReceived(object? sender, MessageEventArgs e)
    {
        if (sender is PipeClientConnection)
        {
            // Register client if this is a registration message
            if (e.Message is { Type: MessageType.System, Content: "CLIENT_REGISTER" })
            {
                RegisterClient(e.Message.Sender, e.Message.Sender, sender);
                return;
            }

            HandleReceivedMessage(e.Message, e.Message.Sender);
        }
    }

    private void OnClientDisconnected(object? sender, ConnectionEventArgs e)
    {
        UnregisterClient(e.Connection.Id);
    }

    private void OnClientErrorOccurred(object? sender, ErrorEventArgs e)
    {
        OnErrorOccurred(e);
    }

    #region Enhanced Client Connection
    private class PipeClientConnection(
        NamedPipeServerStream pipe,
        string pipeName,
        CancellationToken cancellationToken)
        : IDisposable
    {
        private Task? _readTask;
        private bool _disposed;

        public event EventHandler<MessageEventArgs>? MessageReceived;
        public event EventHandler<ConnectionEventArgs>? Disconnected;
        public event EventHandler<ErrorEventArgs>? ErrorOccurred;

        public void StartReading()
        {
            _readTask = Task.Run(ReadMessagesAsync, cancellationToken);
        }

        public async Task<bool> SendMessageAsync(Message message)
        {
            try
            {
                if (_disposed || !pipe.IsConnected)
                    return false;

                var json = JsonSerializer.Serialize(message);
                await using var writer = new StreamWriter(pipe, leaveOpen: true);
                await writer.WriteLineAsync(json);
                await writer.FlushAsync();
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Send error: {ex.Message}", ex));
                return false;
            }
        }

        private async Task ReadMessagesAsync()
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);

                while (!cancellationToken.IsCancellationRequested &&
                       !_disposed &&
                       pipe.IsConnected &&
                       await reader.ReadLineAsync() is { } json)
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<Message>(json);
                        if (message != null)
                        {
                            var connectionInfo = new ConnectionInfo
                            {
                                Id = message.Sender,
                                Name = message.Sender,
                                Address = pipeName
                            };
                            MessageReceived?.Invoke(this, new MessageEventArgs(message, connectionInfo));
                        }
                    }
                    catch (JsonException ex)
                    {
                        ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Message parse error: {ex.Message}", ex));
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested && !_disposed)
                {
                    ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Read error: {ex.Message}", ex));
                }
            }
            finally
            {
                if (!_disposed)
                {
                    var connectionInfo = new ConnectionInfo
                    {
                        Id = "Unknown",
                        Name = "Unknown",
                        Address = pipeName
                    };
                    Disconnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
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
                    pipe.Close();
                    pipe.Dispose();
                }
                catch { /*ignored*/ }

                try
                {
                    _readTask?.Wait(1000);
                }
                catch { /*ignored*/ }
            }
        }
    }
    #endregion
}