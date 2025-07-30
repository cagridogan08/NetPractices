using Messaging.ModelLibrary.Abstract;
using StackExchange.Redis;
using System.Text.Json;

namespace Messaging.ModelLibrary.Redis;
public class RedisClient : MessageClientBase
{
    #region Fields

    private ConnectionMultiplexer? _redis;
    private IDatabase? _database;
    private ISubscriber? _subscriber;
    private RedisConfig? _config;
    private readonly string _clientChannelPrefix = "messaging:client:";
    private readonly string _broadcastChannel = "messaging:broadcast";
    private readonly string _systemChannel = "messaging:system";

    #endregion

    #region Properties

    public override bool IsConnected => _redis?.IsConnected ?? false;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
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
    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            _config = RedisConfig.FromDictionary(configuration);
            var clientName = _config.ClientName;

            var configOptions = ConfigurationOptions.Parse(_config.ConnectionString);
            configOptions.AbortOnConnectFail = false;
            _config.ClientName = clientName;
            _redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
            _database = _redis.GetDatabase();
            _subscriber = _redis.GetSubscriber();

            // Subscribe to client-specific channel and broadcast channel
            var clientChannel = $"{_clientChannelPrefix}{clientName}";
            await _subscriber.SubscribeAsync(clientChannel, OnRedisMessageReceived);
            await _subscriber.SubscribeAsync(_broadcastChannel, OnRedisMessageReceived);
            await _subscriber.SubscribeAsync(_systemChannel, OnRedisMessageReceived);

            ConnectionInfo = new ConnectionInfo
            {
                Id = clientName,
                Name = clientName,
                Address = $"{_config.Host}:{_config.Port}",
                ConnectedAt = DateTime.UtcNow,
                IsActive = true,
                Properties = new Dictionary<string, object>
                {
                    ["Database"] = _config.Database,
                    ["Protocol"] = "Redis"
                }
            };

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
            OnErrorOccurred(new ErrorEventArgs($"Redis connection failed: {ex.Message}", ex));
            return false;
        }
    }



    public override async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected && ConnectionInfo is not null)
            {
                var unregistrationMessage = new Message
                {
                    Content = "CLIENT_UNREGISTER",
                    Sender = ConnectionInfo.Name,
                    Receiver = "System",
                    Type = MessageType.System
                };
                await SendMessageAsync(unregistrationMessage);
            }
            if (_subscriber != null)
            {
                await _subscriber.UnsubscribeAllAsync();
            }

            _redis?.Dispose();
            _redis = null;
            _database = null;
            _subscriber = null;

            if (ConnectionInfo is not null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Redis disconnection error :{e.Message}", e));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected) return false;
            if (_database == null || _subscriber == null)
            {
                OnErrorOccurred(new ErrorEventArgs("Redis client is not connected or initialized."));
                return false;
            }

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var json = JsonSerializer.Serialize(message);

            // FIX: Better channel determination logic
            string channel;
            if (message.Type == MessageType.System)
            {
                channel = _systemChannel;
            }
            else if (string.IsNullOrEmpty(message.Receiver) || message.Receiver == "*")
            {
                channel = _broadcastChannel;
            }
            else if (message.Receiver.StartsWith("group:"))
            {
                var groupName = message.Receiver.Substring(6);
                channel = $"messaging:group:{groupName}";
            }
            else
            {
                // Direct message to specific client
                channel = $"{_clientChannelPrefix}{message.Receiver}";
            }

            var subscribers = await _subscriber.PublishAsync(channel, json);
            return subscribers >= 0;
        }
        catch (Exception e)
        {
            OnErrorOccurred(new ErrorEventArgs($"Redis send message error: {e.Message}", e));
            return false;
        }
    }
    #region Group Management (Override base methods)
    public override async Task<bool> JoinGroupAsync(string groupName)
    {
        try
        {
            if (!IsConnected || _subscriber == null) return false;

            // Subscribe to group channel
            var groupChannel = $"messaging:group:{groupName}";
            await _subscriber.SubscribeAsync(groupChannel, OnRedisMessageReceived);

            // Send join notification
            var joinMessage = new Message
            {
                Content = $"JOIN_GROUP:{groupName}",
                Sender = ConnectionInfo?.Name ?? "Unknown",
                Receiver = "System",
                Type = MessageType.System
            };

            return await SendMessageAsync(joinMessage);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to join group {groupName}: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> LeaveGroupAsync(string groupName)
    {
        try
        {
            if (!IsConnected || _subscriber == null) return false;

            // Send leave notification first
            var leaveMessage = new Message
            {
                Content = $"LEAVE_GROUP:{groupName}",
                Sender = ConnectionInfo?.Name ?? "Unknown",
                Receiver = "System",
                Type = MessageType.System
            };

            var success = await SendMessageAsync(leaveMessage);

            // Unsubscribe from group channel
            var groupChannel = $"messaging:group:{groupName}";
            await _subscriber.UnsubscribeAsync(groupChannel);

            return success;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to leave group {groupName}: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> SendGroupMessageAsync(string groupName, string content)
    {
        var message = new Message
        {
            Content = content,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = $"group:{groupName}",
            Type = MessageType.Text
        };

        try
        {
            if (!IsConnected || _subscriber == null) return false;

            var json = JsonSerializer.Serialize(message);
            var groupChannel = $"messaging:group:{groupName}";

            var subscribers = await _subscriber.PublishAsync(groupChannel, json);
            return subscribers > 0;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send group message: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }
    #endregion

    #region RedisMethods

    private void OnRedisMessageReceived(RedisChannel channel, RedisValue message)
    {
        try
        {
            var messageObj = JsonSerializer.Deserialize<Message>(message!);
            if (messageObj != null && ConnectionInfo != null)
            {
                // Don't process our own messages (except system messages)
                if (messageObj.Sender == ConnectionInfo.Name && messageObj.Type != MessageType.System)
                    return;

                base.OnMessageReceived(new MessageEventArgs(messageObj, ConnectionInfo));
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing received message: {ex.Message}", ex, ConnectionInfo));
        }
    }

    #endregion

    #endregion

}
