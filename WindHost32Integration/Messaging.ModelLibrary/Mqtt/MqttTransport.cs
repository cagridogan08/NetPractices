using Messaging.ModelLibrary.Abstract;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using MQTTnet.Server;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Messaging.ModelLibrary.Mqtt;

/// <summary>
/// MQTT-based message transport implementation following MessageTransportBase pattern
/// </summary>
public class MqttTransport : MessageTransportBase
{
    #region Fields

    private MqttServer? _mqttServer;
    private MqttBrokerConfig? _config;
    private readonly ConcurrentDictionary<string, MqttClientInfo> _mqttClients = new();

    #endregion

    #region Properties

    public override bool IsRunning { get; protected set; }
    public override TransportType TransportType => TransportType.Mqtt;

    #endregion

    #region MessageTransportBase Implementation

    public override async Task<bool> StartAsync(Dictionary<string, object>? configuration = null)
    {
        try
        {
            await StopAsync();

            _config = MqttBrokerConfig.FromDictionary(configuration);

            var factory = new MqttFactory();
            var optionsBuilder = factory.CreateServerOptionsBuilder();

            // Configure endpoints
            ConfigureEndpoints(optionsBuilder, _config);

            // Configure performance settings
            optionsBuilder
                .WithMaxPendingMessagesPerClient(_config.MaxPendingMessages)
                .WithDefaultCommunicationTimeout(_config.CommunicationTimeout);
            var serverOptions = optionsBuilder.Build();
            _mqttServer = factory.CreateMqttServer(serverOptions);
            // Configure authentication
            ConfigureAuthentication(_config);

            // Configure retained messages
            ConfigureRetainedMessages(_config);



            // Subscribe to server events
            SubscribeToServerEvents();

            await _mqttServer.StartAsync();

            IsRunning = true;
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to start MQTT broker: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task StopAsync()
    {
        try
        {
            if (_mqttServer != null)
            {
                UnsubscribeFromServerEvents();
                await _mqttServer.StopAsync();
                _mqttServer.Dispose();
                _mqttServer = null;
            }

            _mqttClients.Clear();
            IsRunning = false;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error stopping MQTT broker: {ex.Message}", ex));
        }
    }

    public override async Task<bool> SendMessageAsync(Message message, string? connectionId = null)
    {
        try
        {
            if (!IsRunning || _mqttServer == null)
                return false;

            if (string.IsNullOrEmpty(connectionId))
                return await BroadcastMessageAsync(message);

            var topic = MqttTopicHelper.GetDirectMessageTopic(connectionId);
            return await PublishMessageToTopic(topic, message, MqttQualityOfServiceLevel.AtLeastOnce);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send MQTT message: {ex.Message}", ex));
            return false;
        }
    }

    public override async Task<bool> BroadcastMessageAsync(Message message)
    {
        try
        {
            if (!IsRunning || _mqttServer == null)
                return false;

            var topic = MqttTopics.BroadcastTopic;
            return await PublishMessageToTopic(topic, message, MqttQualityOfServiceLevel.AtMostOnce);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to broadcast MQTT message: {ex.Message}", ex));
            return false;
        }
    }

    protected override string GetClientAddress(object? transportSpecificData)
    {
        return transportSpecificData is MqttClientInfo clientInfo
            ? clientInfo.Address
            : "Unknown";
    }

    #endregion

    #region MQTT Server Event Handlers

    private void SubscribeToServerEvents()
    {
        if (_mqttServer == null) return;

        _mqttServer.ClientConnectedAsync += OnClientConnectedAsync;
        _mqttServer.ClientDisconnectedAsync += OnClientDisconnectedAsync;
        _mqttServer.InterceptingPublishAsync += OnMessagePublishedAsync;
        _mqttServer.ClientSubscribedTopicAsync += OnClientSubscribedAsync;
    }

    private void UnsubscribeFromServerEvents()
    {
        if (_mqttServer == null) return;

        _mqttServer.ClientConnectedAsync -= OnClientConnectedAsync;
        _mqttServer.ClientDisconnectedAsync -= OnClientDisconnectedAsync;
        _mqttServer.InterceptingPublishAsync -= OnMessagePublishedAsync;
        _mqttServer.ClientSubscribedTopicAsync -= OnClientSubscribedAsync;
    }

    private async Task OnClientConnectedAsync(ClientConnectedEventArgs args)
    {
        try
        {
            var clientInfo = new MqttClientInfo(args.ClientId, args.Endpoint ?? "Unknown");
            _mqttClients[args.ClientId] = clientInfo;

            RegisterClient(args.ClientId, args.ClientId, clientInfo);
            await SubscribeClientToDirectMessages(args.ClientId);
            await NotifyClientJoined(args.ClientId);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling client connection: {ex.Message}", ex));
        }
    }

    private async Task OnClientDisconnectedAsync(ClientDisconnectedEventArgs args)
    {
        try
        {
            if (_mqttClients.TryRemove(args.ClientId, out _))
            {
                UnregisterClient(args.ClientId);
                await NotifyClientLeft(args.ClientId);
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling client disconnection: {ex.Message}", ex));
        }
    }

    private Task OnMessagePublishedAsync(InterceptingPublishEventArgs args)
    {
        try
        {
            var message = MqttMessageConverter.FromMqttMessage(args.ApplicationMessage);
            if (message != null)
            {
                if (_mqttClients.TryGetValue(args.ClientId, out var senderClient))
                {
                    if (message.Sender == senderClient.ClientId && !MqttTopicHelper.IsSystemTopic(args.ApplicationMessage.Topic))
                    {
                        return Task.CompletedTask;
                    }
                }

                UpdateClientActivity(args.ClientId);
                HandleReceivedMessage(message, args.ClientId);
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error processing published message: {ex.Message}", ex));
        }
        return Task.CompletedTask;
    }

    private async Task OnClientSubscribedAsync(ClientSubscribedTopicEventArgs args)
    {
        try
        {
            if (args.TopicFilter.Topic == MqttTopics.BroadcastTopic)
            {
                await SendCurrentClientList(args.ClientId);
            }
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Error handling topic subscription: {ex.Message}", ex));
        }
    }

    #endregion

    #region Helper Methods

    private void ConfigureEndpoints(MqttServerOptionsBuilder optionsBuilder, MqttBrokerConfig config)
    {
        var ipAddress = config.Host == "localhost"
            ? System.Net.IPAddress.Loopback
            : System.Net.IPAddress.Parse(config.Host);

        optionsBuilder.WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(ipAddress)
            .WithDefaultEndpointPort(config.Port);

        if (config.EnableTls && !string.IsNullOrEmpty(config.CertificatePath))
        {
            var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
                config.CertificatePath, config.CertificatePassword);

            optionsBuilder.WithEncryptedEndpoint()
                .WithEncryptedEndpointPort(config.TlsPort)
                .WithEncryptionCertificate(certificate);
        }
    }

    private void ConfigureAuthentication(MqttBrokerConfig config)
    {
        if (config.RequiresAuthentication && _mqttServer is not null)
        {
            _mqttServer.ValidatingConnectionAsync += async context =>
            {
                var isValid = config.IsValidCredentials(context.UserName, context.Password);
                context.ReasonCode = isValid ? MqttConnectReasonCode.Success : MqttConnectReasonCode.BadUserNameOrPassword;

                if (!isValid)
                {
                    OnErrorOccurred(new ErrorEventArgs($"Authentication failed for user: {context.UserName}"));
                }

                await Task.CompletedTask;
            };
        }
    }

    private void ConfigureRetainedMessages(MqttBrokerConfig config)
    {
        if (!config.EnableRetainedMessages && _mqttServer is not null)
        {
            _mqttServer.InterceptingPublishAsync += async context =>
            {
                context.ApplicationMessage.Retain = false;
                await Task.CompletedTask;
            };
        }
    }

    private async Task<bool> PublishMessageToTopic(string topic, Message message, MqttQualityOfServiceLevel qos)
    {
        try
        {
            if (_mqttServer == null || !IsRunning)
                return false;

            var mqttMessage = MqttMessageConverter.ToMqttMessage(topic, message, qos);
            var injectedMessage = new InjectedMqttApplicationMessage(mqttMessage)
            {
                SenderClientId = "System"
            };

            await _mqttServer.InjectApplicationMessage(injectedMessage);
            return true;
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to publish to topic {topic}: {ex.Message}", ex));
            return false;
        }
    }

    private async Task SubscribeClientToDirectMessages(string clientId)
    {
        try
        {
            if (_mqttServer == null) return;

            var topic = MqttTopicHelper.GetDirectMessageTopic(clientId);
            var subscriptions = new List<MqttTopicFilter>
            {
                new MqttTopicFilterBuilder()
                    .WithTopic(topic)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .Build()
            };

            await _mqttServer.SubscribeAsync(clientId, subscriptions);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to subscribe client to direct messages: {ex.Message}", ex));
        }
    }

    private async Task NotifyClientJoined(string clientId)
    {
        var systemMessage = new Message
        {
            Content = $"CLIENT_JOINED:{clientId}",
            Sender = "System",
            Receiver = "*",
            Type = MessageType.System,
            Timestamp = DateTime.UtcNow
        };

        await PublishMessageToTopic(MqttTopics.SystemTopic, systemMessage, MqttQualityOfServiceLevel.AtLeastOnce);
    }

    private async Task NotifyClientLeft(string clientId)
    {
        var systemMessage = new Message
        {
            Content = $"CLIENT_LEFT:{clientId}",
            Sender = "System",
            Receiver = "*",
            Type = MessageType.System,
            Timestamp = DateTime.UtcNow
        };

        await PublishMessageToTopic(MqttTopics.SystemTopic, systemMessage, MqttQualityOfServiceLevel.AtLeastOnce);
    }

    private async Task SendCurrentClientList(string requestingClientId)
    {
        try
        {
            var onlineClients = GetOnlineClientsForBroadcast(requestingClientId);
            var clientListJson = JsonSerializer.Serialize(onlineClients);

            var responseMessage = new Message
            {
                Content = $"CLIENT_LIST:{clientListJson}",
                Sender = "System",
                Receiver = requestingClientId,
                Type = MessageType.System,
                Timestamp = DateTime.UtcNow
            };

            var topic = MqttTopicHelper.GetDirectMessageTopic(requestingClientId);
            await PublishMessageToTopic(topic, responseMessage, MqttQualityOfServiceLevel.AtLeastOnce);
        }
        catch (Exception ex)
        {
            OnErrorOccurred(new ErrorEventArgs($"Failed to send client list: {ex.Message}", ex));
        }
    }

    #endregion

    #region Helper Classes

    private class MqttClientInfo(string clientId, string address)
    {
        public string ClientId { get; } = clientId;
        public string Address { get; } = address;
        public DateTime ConnectedAt { get; } = DateTime.UtcNow;
    }

    #endregion
}