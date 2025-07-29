using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;

namespace Messaging.ModelLibrary.Mqtt;

/// <summary>
/// Message conversion helper for MQTT
/// </summary>
internal static class MqttMessageConverter
{
    public static MqttApplicationMessage ToMqttMessage(string topic, Message message, MqttQualityOfServiceLevel qos)
    {
        var json = JsonSerializer.Serialize(message);
        var payload = Encoding.UTF8.GetBytes(json);

        return new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(qos)
            .WithRetainFlag(false)
            .Build();
    }

    public static Message? FromMqttMessage(MqttApplicationMessage mqttMessage)
    {
        try
        {
            if (mqttMessage.PayloadSegment.Array == null) return null;

            var json = Encoding.UTF8.GetString(mqttMessage.PayloadSegment.Array);
            return JsonSerializer.Deserialize<Message>(json);
        }
        catch
        {
            return null;
        }
    }
}