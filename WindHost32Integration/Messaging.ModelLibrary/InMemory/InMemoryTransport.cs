

using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Messaging.ModelLibrary.InMemory;

public class InMemoryTransport : IMessageTransport
{

    #region Fields

    private static readonly ConcurrentDictionary<string, InMemoryTransport> _servers = new();
    private readonly string _serverName;
    private readonly ConcurrentDictionary<string, InMemoryClientConnection> _connections = new();
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _disposed;

    private readonly Dictionary<string, object> _defaultConfiguration;
    #endregion

    #region Constructor

    public InMemoryTransport(string serverName = "default")
    {
        _serverName = serverName;
        _defaultConfiguration = new Dictionary<string, object>
        {
            { "MaxConnections", 100 },
            { "Timeout", TimeSpan.FromMinutes(5) },
            { "EnableLogging", true }
        };
    }

    #endregion

    #region Events

    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? ClientConnected;
    public event EventHandler<ConnectionEventArgs>? ClientDisconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;

    #endregion

    #region Properties

    public bool IsRunning { get; private set; }
    public TransportType TransportType => TransportType.InMemory;
    public IReadOnlyList<ConnectionInfo> Connections => _connections.Values
        .Select(c => c.Info)
        .ToList();

    #endregion

    #region Methods

    public async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            _cancellationTokenSource = new CancellationTokenSource();

            if (!_servers.TryAdd(_serverName, this))
            {
                throw new InvalidOperationException($"InMemory server '{_serverName}' is already running");
            }

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to start InMemory server: {ex.Message}", ex));
            return false;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();

            // Disconnect all clients
            var connectionTasks = _connections.Values.Select(connection => Task.Run(connection.Disconnect));
            try
            {
                await Task.WhenAll(connectionTasks);
            }
            catch (Exception)
            {
                // Ignore connection disposal errors
            }

            _connections.Clear();
            _servers.TryRemove(_serverName, out _);
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

    internal bool RegisterClient(InMemoryClient client)
    {
        try
        {
            var connectionInfo = new ConnectionInfo
            {
                Id = Guid.NewGuid().ToString(),
                Name = client.ClientName,
                Address = $"inmemory://{_serverName}"
            };

            var clientConnection = new InMemoryClientConnection(client, connectionInfo);
            clientConnection.MessageReceived += OnClientMessageReceived;
            clientConnection.Disconnected += OnClientDisconnected;

            _connections.TryAdd(connectionInfo.Id, clientConnection);
            client.SetConnection(clientConnection);

            ClientConnected?.Invoke(this, new ConnectionEventArgs(connectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ErrorEventArgs($"Failed to register client: {ex.Message}", ex));
            return false;
        }
    }

    internal void UnregisterClient(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            ClientDisconnected?.Invoke(this, new ConnectionEventArgs(connection.Info));
        }
    }

    private void OnClientMessageReceived(object sender, MessageEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnClientDisconnected(object sender, ConnectionEventArgs e)
    {
        _connections.TryRemove(e.Connection.Id, out _);
        ClientDisconnected?.Invoke(this, e);
    }

    internal static InMemoryTransport GetServer(string serverName)
    {
        _servers.TryGetValue(serverName, out var server);
        return server;
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

    #endregion


    internal class InMemoryClientConnection
    {
        private readonly InMemoryClient _client;
        private readonly Channel<Message> _messageChannel;
        private readonly ChannelWriter<Message> _writer;
        private readonly ChannelReader<Message> _reader;
        private bool _disposed;

        public InMemoryClientConnection(InMemoryClient client, ConnectionInfo info)
        {
            _client = client;
            Info = info;

            var options = new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _messageChannel = Channel.CreateBounded<Message>(options);
            _writer = _messageChannel.Writer;
            _reader = _messageChannel.Reader;
        }

        public ConnectionInfo Info { get; }

        public event EventHandler<MessageEventArgs> MessageReceived;
        public event EventHandler<ConnectionEventArgs> Disconnected;

        public async Task<bool> SendMessageAsync(Message message)
        {
            try
            {
                if (_disposed) return false;

                await _writer.WriteAsync(message);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false; // Channel was closed
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void StartReading()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in _reader.ReadAllAsync())
                    {
                        _client.OnMessageReceived(message, Info);
                    }
                }
                catch (Exception)
                {
                    // Channel was closed or disposed
                }
            });
        }

        public void ReceiveFromClient(Message message)
        {
            if (!_disposed)
            {
                MessageReceived?.Invoke(this, new MessageEventArgs(message, Info));
            }
        }

        public void Disconnect()
        {
            if (!_disposed)
            {
                _disposed = true;
                _writer.Complete();
                Disconnected?.Invoke(this, new ConnectionEventArgs(Info));
            }
        }
    }

}