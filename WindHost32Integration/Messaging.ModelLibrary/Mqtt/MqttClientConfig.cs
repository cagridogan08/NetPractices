namespace Messaging.ModelLibrary.Mqtt;

/// <summary>
/// MQTT client configuration helper
/// </summary>
internal class MqttClientConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string ClientName { get; set; } = Environment.UserName;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool UseTls { get; set; } = false;
    public string? WebSocketPath { get; set; }
    public TimeSpan KeepAlivePeriod { get; set; } = TimeSpan.FromSeconds(15);
    public bool CleanSession { get; set; } = true;
    public string? WillTopic { get; set; }
    public string? WillMessage { get; set; }
    public TimeSpan AutoReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }

    public static MqttClientConfig FromDictionary(Dictionary<string, object> configuration)
    {
        return new MqttClientConfig
        {
            Host = configuration.GetValueOrDefault("Host", "localhost") as string ?? "localhost",
            Port = configuration.GetValueOrDefault("Port", 1883) as int? ?? 1883,
            ClientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName,
            Username = configuration.GetValueOrDefault("Username") as string,
            Password = configuration.GetValueOrDefault("Password") as string,
            UseTls = configuration.GetValueOrDefault("UseTls", false) as bool? ?? false,
            WebSocketPath = configuration.GetValueOrDefault("WebSocketPath") as string,
            KeepAlivePeriod = configuration.GetValueOrDefault("KeepAlivePeriod", TimeSpan.FromSeconds(15)) as TimeSpan? ?? TimeSpan.FromSeconds(15),
            CleanSession = configuration.GetValueOrDefault("CleanSession", true) as bool? ?? true,
            WillTopic = configuration.GetValueOrDefault("WillTopic") as string,
            WillMessage = configuration.GetValueOrDefault("WillMessage") as string,
            AutoReconnectDelay = configuration.GetValueOrDefault("AutoReconnectDelay", TimeSpan.FromSeconds(5)) as TimeSpan? ?? TimeSpan.FromSeconds(5),
            CertificatePath = configuration.GetValueOrDefault("CertificatePath") as string,
            CertificatePassword = configuration.GetValueOrDefault("CertificatePassword") as string
        };
    }
}