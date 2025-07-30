namespace Messaging.ModelLibrary.Redis;

/// <summary>
/// Configuration helper for Redis
/// </summary>
internal class RedisConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6379;
    public string? Password { get; set; }
    public int Database { get; set; } = 0;
    public string ClientName { get; set; } = Environment.UserName;
    public int ConnectTimeout { get; set; } = 5000;
    public int SyncTimeout { get; set; } = 5000;

    public string ConnectionString =>
        $"{Host}:{Port}" +
        (string.IsNullOrEmpty(Password) ? "" : $",password={Password}") +
        $",connectTimeout={ConnectTimeout},syncTimeout={SyncTimeout},defaultDatabase={Database}";

    public static RedisConfig FromDictionary(Dictionary<string, object>? configuration)
    {
        if (configuration == null) return new RedisConfig();

        return new RedisConfig
        {
            Host = configuration.GetValueOrDefault("Host", "localhost") as string ?? "localhost",
            Port = configuration.GetValueOrDefault("Port", 6379) as int? ?? 6379,
            Password = configuration.GetValueOrDefault("Password") as string,
            Database = configuration.GetValueOrDefault("Database", 0) as int? ?? 0,
            ClientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName,
            ConnectTimeout = configuration.GetValueOrDefault("ConnectTimeout", 5000) as int? ?? 5000,
            SyncTimeout = configuration.GetValueOrDefault("SyncTimeout", 5000) as int? ?? 5000
        };
    }
}