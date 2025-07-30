using Messaging.ModelLibrary.Abstract;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Messaging.ModelLibrary.Redis;
public class RedisTransport : MessageTransportBase
{
    #region Fields

    private ConnectionMultiplexer? _redis;
    private IDatabase? _database;
    private ISubscriber? _subscriber;
    private readonly ConcurrentDictionary<string, RedisClientInfo> _clients = new();
    private RedisConfig? _config;
    private readonly string _clientsSetKey = "messaging:clients";
    private readonly string _clientChannelPrefix = "messaging:client:";
    private readonly string _broadcastChannel = "messaging:broadcast";
    private readonly string _systemChannel = "messaging:system";

    #endregion

    #region Properties

    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.Redis;

    #endregion

    #region Methods

    /// <summary>
    /// Initializes and starts the Redis transport using the provided configuration.
    /// Establishes connection to Redis, sets up the database and subscriber,
    /// and subscribes to the system message channel.
    /// </summary>
    /// <param name="configuration">
    /// A dictionary containing configuration keys such as:
    /// "Host" (string), "Port" (int), "Password" (string, optional),
    /// "Database" (int), "ClientName" (string), "ConnectTimeout" (int), and "SyncTimeout" (int).
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation. 
    /// The task result contains true if the transport started successfully; otherwise, false.
    /// </returns>
    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync(); // Ensure any previous instance is stopped
            _config = RedisConfig.FromDictionary(configuration);
            if (_config == null)
            {
                OnErrorOccurred(new ErrorEventArgs("Redis configuration is null or invalid."));
                return false;
            }
            var configOptions = ConfigurationOptions.Parse(_config.ConnectionString);
            configOptions.AbortOnConnectFail = false;

            _redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
            _database = _redis.GetDatabase(_config.Database);
            _subscriber = _redis.GetSubscriber();

            await _subscriber.SubscribeAsync(new RedisChannel(_systemChannel, RedisChannel.PatternMode.Auto), OnSystemMessageReceived);
            IsRunning = true;
            return true;
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error while starting RedisTransport :{e.Message}", e));
        }

        return false;
    }

    #region RedisMethods

    private async void OnSystemMessageReceived(RedisChannel channel, RedisValue message)
    {
        try
        {
            if (message.IsNull) return;
            var messageObj = JsonSerializer.Deserialize<Message>(message);
            if (messageObj is { Type: MessageType.System, Content: "CLIENT_REGISTER" })
            {
                await RegisterRedisClient(messageObj.Sender);
            }
            else if (messageObj is { Type: MessageType.System, Content: "CLIENT_UNREGISTER" })
            {
                await UnregisterRedisClient(messageObj.Sender);
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing system message: {ex.Message}", ex));
        }
    }

    private async Task RegisterRedisClient(string clientName)
    {
        try
        {
            if (_database == null || _clients.ContainsKey(clientName)) return;

            // Add client to Redis set
            await _database.SetAddAsync(_clientsSetKey, clientName);

            // Store client info
            var clientInfo = new RedisClientInfo(clientName, $"redis://{_config?.Host}:{_config?.Port}");
            _clients[clientName] = clientInfo;

            var connectionInfo = new ConnectionInfo
            {
                Id = clientName,
                Name = clientName,
                Address = clientInfo.Address,
                ConnectedAt = DateTime.UtcNow,
                IsActive = true
            };

            RegisterClient(clientName, clientName, clientInfo);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error registering Redis client: {ex.Message}", ex));
        }
    }

    private async Task UnregisterRedisClient(string clientName)
    {
        try
        {
            if (_database == null) return;

            // Remove client from Redis set
            await _database.SetRemoveAsync(_clientsSetKey, clientName);

            _clients.TryRemove(clientName, out _);
            UnregisterClient(clientName);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error unregistering Redis client: {ex.Message}", ex));
        }
    }

    #endregion


    public override async Task StopAsync()
    {
        try
        {
            IsRunning = false;
            if (_subscriber is not null)
            {
                await _subscriber.UnsubscribeAllAsync();
            }
            if (_database is not null)
            {
                // Optionally clear the clients set
                await _database.KeyDeleteAsync(_clientsSetKey);
            }
            if (_redis is not null)
            {
                await _redis.CloseAsync();
                await _redis.DisposeAsync();
                _redis = null;
                _subscriber = null;
                _database = null;
            }
            _clients.Clear();
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error while stopping Redis transport :{e.Message}", e));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (!IsRunning) return false;
            if (_subscriber is null) return false;
            var json = JsonSerializer.Serialize(message);
            var channel = string.IsNullOrEmpty(connectionId)
                ? _broadcastChannel
                : $"{_clientChannelPrefix}{connectionId}";

            var subscribers = await _subscriber.PublishAsync(channel, json);
            return subscribers > 0;
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send Redis messagei :{e.Message}", e));
        }
        return false;
    }

    public override async Task<bool> BroadcastMessageAsync(Message message) => await SendMessageAsync(message);


    protected override string GetClientAddress(object? transportSpecificData)
    {
        return transportSpecificData is RedisClientInfo client
            ? client.Address
            : "redis://unknown";
    }

    #endregion

    #region ClientInfo

    private class RedisClientInfo(string clientName, string address)
    {
        public string ClientName { get; } = clientName;
        public string Address { get; } = address;
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    }

    #endregion
}


