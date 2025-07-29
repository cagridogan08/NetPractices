
namespace Messaging.ModelLibrary.Mqtt
{
    internal static class MqttTopics
    {
        internal const string SystemTopic = "messaging/system";
        internal const string ClientDiscoveryTopic = "messaging/discovery";
        internal const string DirectMessageTopic = "messaging/direct";
        internal const string BroadcastTopic = "messaging/broadcast";
        internal const string GroupTopicPrefix = "messaging/groups";
    }
    internal static class MqttTopicHelper
    {
        public static string GetDirectMessageTopic(string clientId)
        {
            return $"{MqttTopics.DirectMessageTopic}/{clientId}";
        }

        public static string GetGroupTopic(string groupName)
        {
            return $"{MqttTopics.GroupTopicPrefix}/{groupName}";
        }

        public static bool IsSystemTopic(string topic)
        {
            return topic.StartsWith(MqttTopics.SystemTopic) || topic.StartsWith("messaging/discovery");
        }
    }
}
