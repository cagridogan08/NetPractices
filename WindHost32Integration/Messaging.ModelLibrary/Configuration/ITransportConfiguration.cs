using System.Net;
namespace Messaging.ModelLibrary.Configuration;
/// <summary>
/// Marker interface for any transport configuration.
/// </summary>
public interface ITransportConfiguration
{
    TransportType TransportType { get; }
}

/// <summary>
/// Strongly‐typed transport configuration.
/// </summary>
/// <typeparam name="TSettings">The concrete settings type.</typeparam>
public interface ITransportConfiguration<out TSettings>
    : ITransportConfiguration
{
    TSettings Settings { get; }
}
public abstract record TransportConfiguration<TSettings>(TSettings Settings) : ITransportConfiguration<TSettings>
{
    public abstract TransportType TransportType { get; }
}

public abstract record ServerConfiguration<TSettings>(TSettings Settings) : TransportConfiguration<TSettings>(Settings);

public abstract record ClientConfiguration<TSettings>(TSettings Settings) : TransportConfiguration<TSettings>(Settings);


#region ServerConfiguration

public record PipeServerConfiguration(string? PipeName = "GenericPipeName");

public record TcpServerConfiguration(string? Host = "localhost", int? Port = 8080);

public record UdpServerConfiguration(string? Host = "localhost", int? Port = 8080);

public record WebSocketServerConfiguration(string? Host = "localhost", int? Port = 8080, string? Path = "/");

public record SignalRServerConfiguration(
    string? Address = "localhost",
    int? Port = 5003,
    string? HubPath = "/messagingHub",
    bool? EnableHttps = false,
    string? CertificatePath = null,
    string? CertificatePassword = null,
    bool? EnableCors = true,
    string[]? CorsOrigins = null!,
    bool? EnableDetailedErrors = false,
    int? MaxBufferSize = 32 * 1024,
    int? KeepAliveInterval = 15,
    int? ClientTimeoutInterval = 30
)
{
    public SignalRServerConfiguration() : this(
        Address: "localhost",
        Port: 5003,
        HubPath: "/messagingHub",
        EnableHttps: false,
        CertificatePath: null,
        CertificatePassword: null,
        EnableCors: true,
        CorsOrigins: ["*"],
        EnableDetailedErrors: false,
        MaxBufferSize: 32 * 1024,
        KeepAliveInterval: 15,
        ClientTimeoutInterval: 30
    )
    { }
}

public record GrpcServerConfiguration(string? Address = "localhost", int? Port = 5000
, bool? EnableHttps = false, string? CertificatePath = null, string? CertificatePassword = null);

public record RabbitMqServerConfiguration(
    string? Host = "localhost",
    int? Port = 5672,
    string? VirtualHost = "/",
    string? Username = "guest");

public record RtpServerConfiguration(
    string? Address = "localhost",
    int? Port = 5004,
    int? ReceiveTimeout = 1000,
    int? SessionTimeout = 30,
    bool? EnableMulticats = false,
    string? MulticastAddress = null)
{
    public IPAddress BindAddress => IPAddress.TryParse(Address, out var bindAddress) ? bindAddress : IPAddress.Any;
}

public record MqttServerConfiguration(
    string Host = "localhost",
    int Port = 1883,
    int TlsPort = 8883,
    bool EnableTls = false,
    string? CertificatePath = null,
    string? CertificatePassword = null,
    string? Username = null,
    string? Password = null,
    bool EnableRetainedMessages = true,
    int MaxPendingMessages = 250
)
{
    public TimeSpan CommunicationTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public bool RequiresAuthentication
        => !string.IsNullOrEmpty(Username);

    public bool IsValidCredentials(string? username, string? password)
        => username == Username && password == Password;
}

// <summary>
/// Configuration helper for ZeroMQ
/// </summary>
internal class ZeroMqConfig
{
    public string Host { get; set; } = "localhost";
    public int RouterPort { get; set; } = 5555;
    public int PublisherPort { get; set; } = 5556;
    public string ClientName { get; set; } = Environment.UserName;
    public int SendTimeout { get; set; } = 5000;
    public int ReceiveTimeout { get; set; } = 5000;

    public static ZeroMqConfig FromDictionary(Dictionary<string, object>? configuration)
    {
        if (configuration == null) return new ZeroMqConfig();

        return new ZeroMqConfig
        {
            Host = configuration.GetValueOrDefault("Host", "localhost") as string ?? "localhost",
            RouterPort = configuration.GetValueOrDefault("RouterPort", 5555) as int? ?? 5555,
            PublisherPort = configuration.GetValueOrDefault("PublisherPort", 5556) as int? ?? 5556,
            ClientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName,
            SendTimeout = configuration.GetValueOrDefault("SendTimeout", 5000) as int? ?? 5000,
            ReceiveTimeout = configuration.GetValueOrDefault("ReceiveTimeout", 5000) as int? ?? 5000
        };
    }
}


#endregion


#region Clients

public record PipeClientSettings(string? PipeName = "GenericPipeName", string? ClientName = null)
{
    public string ClientName { get; set; } = ClientName ?? Environment.UserName;
}

#endregion
