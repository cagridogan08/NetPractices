using Messaging.ModelLibrary.Abstract;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Text;

namespace Messaging.ModelLibrary.Mqtt;

/// <summary>
/// MQTT-based message client implementation following MessageClientBase pattern
/// </summary>
public class MqttMessageClient : MessageClientBase
{
    #region Fields

    private IManagedMqttClient? _mqttClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private MqttClientConfig? _config;

    #endregion

    #region Properties

    public override bool IsConnected => _mqttClient?.IsConnected == true;
    public override ConnectionInfo? ConnectionInfo { get; protected set; }
    public override TransportType TransportType => TransportType.Mqtt;

    #endregion

    #region MessageClientBase Implementation

    public override async Task<bool> ConnectAsync(Dictionary<string, object> configuration)
    {
        try
        {
            await DisconnectAsync();

            _config = MqttClientConfig.FromDictionary(configuration);
            _cancellationTokenSource = new CancellationTokenSource();

            var clientOptions = BuildClientOptions(_config);
            var managedOptions = BuildManagedOptions(clientOptions, _config);

            var factory = new MqttFactory();
            _mqttClient = factory.CreateManagedMqttClient();

            SubscribeToClientEvents();

            await _mqttClient.StartAsync(managedOptions);

            // Wait for connection
            if (!await WaitForConnection())
            {
                throw new TimeoutException("Failed to connect to MQTT broker within timeout period");
            }

            ConnectionInfo = CreateConnectionInfo(_config, clientOptions);
            await SetupClientSubscriptions();
            await SendRegistrationMessage();

            OnConnected(new ConnectionEventArgs(ConnectionInfo));
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"MQTT connection failed: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task DisconnectAsync()
    {
        try
        {
            if (IsConnected && ConnectionInfo != null)
            {
                await SendUnregistrationMessage();
            }

            _cancellationTokenSource?.Cancel();

            if (_mqttClient != null)
            {
                UnsubscribeFromClientEvents();
                await _mqttClient.StopAsync();
                _mqttClient.Dispose();
                _mqttClient = null;
            }

            if (ConnectionInfo != null)
            {
                OnDisconnected(new ConnectionEventArgs(ConnectionInfo));
                ConnectionInfo = null;
            }

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"MQTT disconnection error: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message)
    {
        try
        {
            if (!IsConnected || _mqttClient == null || _disposed)
                return false;

            if (string.IsNullOrEmpty(message.Sender))
                message.Sender = ConnectionInfo?.Name ?? "Unknown";

            var (topic, qos) = DetermineTopicAndQos(message);
            var mqttMessage = MqttMessageConverter.ToMqttMessage(topic, message, qos);

            await _mqttClient.EnqueueAsync(mqttMessage);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"MQTT send error: {ex.Message}", ex, ConnectionInfo));
            return false;
        }
    }

    #endregion

    #region Group Management (Override base methods)

    public override async Task<bool> JoinGroupAsync(string groupName)
    {
        try
        {
            if (!IsConnected || _mqttClient == null)
                return false;

            // Subscribe to group topic
            var groupTopic = MqttTopicHelper.GetGroupTopic(groupName);
            await SubscribeToTopic(groupTopic, MqttQualityOfServiceLevel.AtLeastOnce);

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
            if (!IsConnected || _mqttClient == null)
                return false;

            // Send leave notification first
            var leaveMessage = new Message
            {
                Content = $"LEAVE_GROUP:{groupName}",
                Sender = ConnectionInfo?.Name ?? "Unknown",
                Receiver = "System",
                Type = MessageType.System
            };

            var success = await SendMessageAsync(leaveMessage);

            // Unsubscribe from group topic
            var groupTopic = MqttTopicHelper.GetGroupTopic(groupName);
            await UnsubscribeFromTopic(groupTopic);

            return success;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to leave group {groupName}: {ex.Message}", ex));
            return false;
        }
    }

    #endregion

    #region MQTT Client Events

    private void SubscribeToClientEvents()
    {
        if (_mqttClient == null) return;

        _mqttClient.ConnectedAsync += OnConnectedAsync;
        _mqttClient.DisconnectedAsync += OnDisconnectedAsync;
        _mqttClient.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
        _mqttClient.ConnectingFailedAsync += OnConnectingFailedAsync;
    }

    private void UnsubscribeFromClientEvents()
    {
        if (_mqttClient == null) return;

        _mqttClient.ConnectedAsync -= OnConnectedAsync;
        _mqttClient.DisconnectedAsync -= OnDisconnectedAsync;
        _mqttClient.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
        _mqttClient.ConnectingFailedAsync -= OnConnectingFailedAsync;
    }

    private Task OnConnectedAsync(MqttClientConnectedEventArgs args)
    {
        // Connection handling is done in ConnectAsync
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        if (!_disposed && ConnectionInfo != null && args.Exception != null)
        {
            OnErrorOccurred(new ErrorEventArgs($"MQTT disconnected: {args.Exception.Message}", args.Exception, ConnectionInfo));
        }
        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var message = MqttMessageConverter.FromMqttMessage(args.ApplicationMessage);

            if (message != null && ConnectionInfo != null)
            {
                // Don't process our own messages (except system messages)
                if (message.Sender == ConnectionInfo.Name && message.Type != MessageType.System)
                    return Task.CompletedTask;

                OnMessageReceived(new MessageEventArgs(message, ConnectionInfo));
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing received message: {ex.Message}", ex, ConnectionInfo));
        }
        return Task.CompletedTask;
    }

    private Task OnConnectingFailedAsync(ConnectingFailedEventArgs args)
    {
        OnErrorOccurred(new ErrorEventArgs($"MQTT connection failed: {args.Exception?.Message}", args.Exception, ConnectionInfo));
        return Task.CompletedTask;
    }

    #endregion

    #region Helper Methods

    private MqttClientOptions BuildClientOptions(MqttClientConfig config)
    {
        var clientOptionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"{config.ClientName}_{Guid.NewGuid():N}")
            .WithKeepAlivePeriod(config.KeepAlivePeriod)
            .WithCleanSession(config.CleanSession);

        // Configure connection method (WebSocket or TCP)
        if (!string.IsNullOrEmpty(config.WebSocketPath))
        {
            var wsUri = $"{(config.UseTls ? "wss" : "ws")}://{config.Host}:{config.Port}{config.WebSocketPath}";
            clientOptionsBuilder.WithWebSocketServer(options => { options.WithUri(wsUri); });
        }
        else
        {
            clientOptionsBuilder.WithTcpServer(config.Host, config.Port);
        }

        // Configure authentication
        if (!string.IsNullOrEmpty(config.Username))
        {
            clientOptionsBuilder.WithCredentials(config.Username, config.Password ?? string.Empty);
        }

        // Configure Last Will message
        if (!string.IsNullOrEmpty(config.WillTopic))
        {
            var willPayload = Encoding.UTF8.GetBytes(config.WillMessage ?? $"CLIENT_DISCONNECTED:{config.ClientName}");
            clientOptionsBuilder.WithWillTopic(config.WillTopic)
                .WithWillPayload(willPayload)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithWillRetain(false);
        }

        // Configure TLS options (new method for MQTTnet v4+)
        if (config.UseTls)
        {
            var tlsOptions = new MqttClientTlsOptions
            {
                UseTls = true,
                AllowUntrustedCertificates = true,
                IgnoreCertificateChainErrors = true,
                IgnoreCertificateRevocationErrors = true,
                CertificateValidationHandler = _ => true
            };

            clientOptionsBuilder.WithTlsOptions(tlsOptions);

            clientOptionsBuilder.WithTlsOptions(tlsOptions);
        }

        return clientOptionsBuilder.Build();
    }


    private ManagedMqttClientOptions BuildManagedOptions(MqttClientOptions clientOptions, MqttClientConfig config)
    {
        return new ManagedMqttClientOptionsBuilder()
            .WithClientOptions(clientOptions)
            .WithAutoReconnectDelay(config.AutoReconnectDelay)
            .WithMaxPendingMessages(100)
            .WithPendingMessagesOverflowStrategy(MqttPendingMessagesOverflowStrategy.DropOldestQueuedMessage)
            .Build();
    }

    private async Task<bool> WaitForConnection()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var startTime = DateTime.UtcNow;

        while (!IsConnected && DateTime.UtcNow - startTime < timeout &&
               _cancellationTokenSource?.Token.IsCancellationRequested != true)
        {
            await Task.Delay(100, _cancellationTokenSource?.Token ?? CancellationToken.None);
        }

        return IsConnected;
    }

    private ConnectionInfo CreateConnectionInfo(MqttClientConfig config, MqttClientOptions clientOptions)
    {
        return new ConnectionInfo
        {
            Id = config.ClientName,
            Name = config.ClientName,
            Address = $"{config.Host}:{config.Port}",
            ConnectedAt = DateTime.UtcNow,
            IsActive = true,
            Properties = new Dictionary<string, object>
            {
                ["Protocol"] = "MQTT",
                ["ClientId"] = clientOptions.ClientId,
                ["KeepAlivePeriod"] = config.KeepAlivePeriod.TotalSeconds,
                ["CleanSession"] = config.CleanSession,
                ["UseTls"] = config.UseTls,
                ["IsWebSocket"] = !string.IsNullOrEmpty(config.WebSocketPath)
            }
        };
    }

    private async Task SetupClientSubscriptions()
    {
        if (_mqttClient == null || ConnectionInfo == null) return;

        try
        {
            var factory = new MqttFactory();
            var subscriptions = new List<MqttTopicFilter>
            {
                // System messages
                factory.CreateTopicFilterBuilder()
                    .WithTopic(MqttTopics.SystemTopic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build(),
                    
                // Broadcast messages
                factory.CreateTopicFilterBuilder()
                    .WithTopic(MqttTopics.BroadcastTopic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .Build(),
                    
                // Direct messages for this client
                factory.CreateTopicFilterBuilder()
                    .WithTopic(MqttTopicHelper.GetDirectMessageTopic(ConnectionInfo.Name))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build()
            };

            await _mqttClient.SubscribeAsync(subscriptions);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to setup subscriptions: {ex.Message}", ex));
        }
    }

    private async Task SubscribeToTopic(string topic, MqttQualityOfServiceLevel qos)
    {
        try
        {
            if (_mqttClient == null) return;

            var factory = new MqttFactory();
            var subscribeOptions = factory.CreateTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(qos)
                .Build();

            await _mqttClient.SubscribeAsync(new[] { subscribeOptions });
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to subscribe to topic {topic}: {ex.Message}", ex));
        }
    }

    private async Task UnsubscribeFromTopic(string topic)
    {
        try
        {
            if (_mqttClient == null) return;
            await _mqttClient.UnsubscribeAsync(new[] { topic });
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to unsubscribe from topic {topic}: {ex.Message}", ex));
        }
    }

    private (string topic, MqttQualityOfServiceLevel qos) DetermineTopicAndQos(Message message)
    {
        if (string.IsNullOrEmpty(message.Receiver) || message.Receiver == "*")
        {
            return (MqttTopics.BroadcastTopic, MqttQualityOfServiceLevel.AtMostOnce);
        }
        else if (message.Receiver.StartsWith("group:"))
        {
            var groupName = message.Receiver.Substring(6);
            return (MqttTopicHelper.GetGroupTopic(groupName), MqttQualityOfServiceLevel.AtLeastOnce);
        }
        else if (message.Type == MessageType.System)
        {
            return (MqttTopics.SystemTopic, MqttQualityOfServiceLevel.AtLeastOnce);
        }
        else
        {
            return (MqttTopicHelper.GetDirectMessageTopic(message.Receiver), MqttQualityOfServiceLevel.AtLeastOnce);
        }
    }

    private async Task SendRegistrationMessage()
    {
        var registrationMessage = new Message
        {
            Content = "CLIENT_REGISTER",
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = "System",
            Type = MessageType.System
        };

        await SendMessageAsync(registrationMessage);
    }

    private async Task SendUnregistrationMessage()
    {
        var unregistrationMessage = new Message
        {
            Content = "CLIENT_UNREGISTER",
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Receiver = "System",
            Type = MessageType.System
        };

        await SendMessageAsync(unregistrationMessage);
    }

    #endregion

    #region Disposal

    public override void Dispose()
    {
        if (!_disposed)
        {
            base.Dispose();

            try
            {
                DisconnectAsync().Wait(3000);
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    #endregion
}