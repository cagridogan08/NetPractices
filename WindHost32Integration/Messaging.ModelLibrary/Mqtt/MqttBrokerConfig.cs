namespace Messaging.ModelLibrary.Mqtt;

/// <summary>
/// MQTT broker configuration helper
/// </summary>
internal class MqttBrokerConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public int TlsPort { get; set; } = 8883;
    public bool EnableTls { get; set; } = false;
    public string? CertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool EnableRetainedMessages { get; set; } = true;
    public int MaxPendingMessages { get; set; } = 250;
    public TimeSpan CommunicationTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public bool RequiresAuthentication => !string.IsNullOrEmpty(Username);

    public bool IsValidCredentials(string? username, string? password)
    {
        return username == Username && password == Password;
    }

    public static MqttBrokerConfig FromDictionary(Dictionary<string, object>? configuration)
    {
        if (configuration == null) return new MqttBrokerConfig();

        return new MqttBrokerConfig
        {
            Host = configuration.GetValueOrDefault("Host", "localhost") as string ?? "localhost",
            Port = configuration.GetValueOrDefault("Port", 1883) as int? ?? 1883,
            TlsPort = configuration.GetValueOrDefault("TlsPort", 8883) as int? ?? 8883,
            EnableTls = configuration.GetValueOrDefault("EnableTls", false) as bool? ?? false,
            CertificatePath = configuration.GetValueOrDefault("CertificatePath") as string,
            CertificatePassword = configuration.GetValueOrDefault("CertificatePassword") as string,
            Username = configuration.GetValueOrDefault("Username") as string,
            Password = configuration.GetValueOrDefault("Password") as string,
            EnableRetainedMessages = configuration.GetValueOrDefault("EnableRetainedMessages", true) as bool? ?? true,
            MaxPendingMessages = configuration.GetValueOrDefault("MaxPendingMessagesPerClient", 250) as int? ?? 250,
            CommunicationTimeout = configuration.GetValueOrDefault("DefaultCommunicationTimeout", TimeSpan.FromSeconds(15)) as TimeSpan? ?? TimeSpan.FromSeconds(15)
        };
    }
}