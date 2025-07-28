namespace Messaging.ModelLibrary.Helpers;

/// <summary>
/// Factory for creating messaging service configurations
/// </summary>
public static class MessagingConfigurationFactory
{
    #region Transport Configurations
    public static Dictionary<string, object> CreateTcpServerConfig(string host = "localhost", int port = 8080)
    {
        return new Dictionary<string, object>
        {
            ["Host"] = host,
            ["Port"] = port
        };
    }

    public static Dictionary<string, object> CreateTcpClientConfig(string host = "localhost", int port = 8080, string clientName = null)
    {
        return new Dictionary<string, object>
        {
            ["Host"] = host,
            ["Port"] = port,
            ["ClientName"] = clientName ?? Environment.UserName,
            ["Timeout"] = 5000
        };
    }

    public static Dictionary<string, object> CreateUdpServerConfig(string host = "localhost", int port = 8080)
    {
        return new Dictionary<string, object>
        {
            ["Host"] = host,
            ["Port"] = port
        };
    }

    public static Dictionary<string, object> CreateUdpClientConfig(string host = "localhost", int port = 8080, string clientName = null, int localPort = 0)
    {
        return new Dictionary<string, object>
        {
            ["Host"] = host,
            ["Port"] = port,
            ["LocalPort"] = localPort,
            ["ClientName"] = clientName ?? Environment.UserName
        };
    }

    public static Dictionary<string, object> CreateWebSocketServerConfig(string host = "localhost", int port = 8080, string path = "/")
    {
        return new Dictionary<string, object>
        {
            ["Host"] = host,
            ["Port"] = port,
            ["Path"] = path
        };
    }

    public static Dictionary<string, object> CreateWebSocketClientConfig(string host = "localhost", int port = 8080, string path = "/", string clientName = null, bool useSSL = false)
    {
        return new Dictionary<string, object>
        {
            ["Host"] = host,
            ["Port"] = port,
            ["Path"] = path,
            ["ClientName"] = clientName ?? Environment.UserName,
            ["UseSSL"] = useSSL,
            ["Timeout"] = 5000
        };
    }

    public static Dictionary<string, object> CreateNamedPipeServerConfig(string pipeName = "GenericMessagingApp")
    {
        return new Dictionary<string, object>
        {
            ["PipeName"] = pipeName
        };
    }

    public static Dictionary<string, object> CreateNamedPipeClientConfig(string pipeName = "GenericMessagingApp", string clientName = null, string serverName = ".")
    {
        return new Dictionary<string, object>
        {
            ["PipeName"] = pipeName,
            ["ClientName"] = clientName ?? Environment.UserName,
            ["ServerName"] = serverName,
            ["Timeout"] = 5000
        };
    }

    public static Dictionary<string, object> CreateSignalRServerConfig(string address = "localhost", int port = 5003, string hubPath = "/messagingHub")
    {
        return new Dictionary<string, object>
        {
            ["Address"] = address,
            ["Port"] = port,
            ["HubPath"] = hubPath,
            ["EnableCors"] = true,
            ["EnableDetailedErrors"] = false
        };
    }

    public static Dictionary<string, object> CreateSignalRClientConfig(string serverUrl, string clientName = null, string accessToken = null)
    {
        var config = new Dictionary<string, object>
        {
            ["ServerUrl"] = serverUrl,
            ["ClientName"] = clientName ?? Environment.UserName,
            ["EnableAutoReconnect"] = true,
            ["Transport"] = "WebSockets"
        };

        if (!string.IsNullOrEmpty(accessToken))
        {
            config["AccessToken"] = accessToken;
        }

        return config;
    }

    public static Dictionary<string, object> CreateGrpcServerConfig(string address = "localhost", int port = 5000, bool enableHttps = false)
    {
        return new Dictionary<string, object>
        {
            ["Address"] = address,
            ["Port"] = port,
            ["EnableHttps"] = enableHttps
        };
    }

    public static Dictionary<string, object> CreateGrpcClientConfig(string serverAddress, string clientName = null, bool disableCertValidation = false)
    {
        return new Dictionary<string, object>
        {
            ["ServerAddress"] = serverAddress,
            ["ClientName"] = clientName ?? Environment.UserName,
            ["MaxReceiveMessageSize"] = 4 * 1024 * 1024,
            ["DisableCertificateValidation"] = disableCertValidation
        };
    }

    public static Dictionary<string, object> CreateRtpServerConfig(int port = 5004, string bindAddress = null)
    {
        return new Dictionary<string, object>
        {
            ["Port"] = port,
            ["BindAddress"] = bindAddress ?? "0.0.0.0",
            ["ReceiveTimeout"] = 1000,
            ["SessionTimeout"] = 30
        };
    }

    public static Dictionary<string, object> CreateRtpClientConfig(string serverAddress, int serverPort, string clientName = null, int localPort = 0)
    {
        return new Dictionary<string, object>
        {
            ["ServerAddress"] = serverAddress,
            ["ServerPort"] = serverPort,
            ["ClientName"] = clientName ?? Environment.UserName,
            ["LocalPort"] = localPort,
            ["ReceiveTimeout"] = 5000,
            ["SendTimeout"] = 5000
        };
    }
    #endregion

    #region Advanced Configurations
    public static Dictionary<string, object> CreateSecureWebSocketConfig(string host, int port, string certPath, string certPassword, string path = "/")
    {
        return new Dictionary<string, object>
        {
            ["Host"] = host,
            ["Port"] = port,
            ["Path"] = path,
            ["UseSSL"] = true,
            ["CertificatePath"] = certPath,
            ["CertificatePassword"] = certPassword
        };
    }

    public static Dictionary<string, object> CreateHighPerformanceConfig(TransportType transportType, int maxConnections = 1000, int bufferSize = 8192)
    {
        var config = transportType switch
        {
            TransportType.Tcp => CreateTcpServerConfig(),
            TransportType.Udp => CreateUdpServerConfig(),
            TransportType.WebSocket => CreateWebSocketServerConfig(),
            TransportType.SignalR => CreateSignalRServerConfig(),
            TransportType.gRPC => CreateGrpcServerConfig(),
            TransportType.NamedPipe => CreateNamedPipeServerConfig(),
            _ => throw new ArgumentException($"Unsupported transport type: {transportType}")
        };

        config["MaxConnections"] = maxConnections;
        config["BufferSize"] = bufferSize;
        config["EnableKeepAlive"] = true;
        config["KeepAliveInterval"] = 30;
        config["ReceiveTimeout"] = 30000;
        config["SendTimeout"] = 30000;

        return config;
    }
    #endregion
}