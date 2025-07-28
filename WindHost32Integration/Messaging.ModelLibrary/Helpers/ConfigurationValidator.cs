namespace Messaging.ModelLibrary.Helpers;

/// <summary>
/// Configuration validation utilities
/// </summary>
public static class ConfigurationValidator
{
    public static bool ValidateServerConfig(Dictionary<string, object> config, TransportType transportType)
    {
        try
        {
            return transportType switch
            {
                TransportType.Tcp => ValidateTcpServerConfig(config),
                TransportType.Udp => ValidateUdpServerConfig(config),
                TransportType.WebSocket => ValidateWebSocketServerConfig(config),
                TransportType.SignalR => ValidateSignalRServerConfig(config),
                TransportType.gRPC => ValidateGrpcServerConfig(config),
                TransportType.NamedPipe => ValidateNamedPipeServerConfig(config),
                TransportType.Rtp => ValidateRtpServerConfig(config),
                _ => false
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool ValidateClientConfig(Dictionary<string, object> config, TransportType transportType)
    {
        try
        {
            return transportType switch
            {
                TransportType.Tcp => ValidateTcpClientConfig(config),
                TransportType.Udp => ValidateUdpClientConfig(config),
                TransportType.WebSocket => ValidateWebSocketClientConfig(config),
                TransportType.SignalR => ValidateSignalRClientConfig(config),
                TransportType.gRPC => ValidateGrpcClientConfig(config),
                TransportType.NamedPipe => ValidateNamedPipeClientConfig(config),
                TransportType.Rtp => ValidateRtpClientConfig(config),
                _ => false
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ValidateTcpServerConfig(Dictionary<string, object> config)
    {
        var port = config.GetValueOrDefault("Port", 8080) as int? ?? 8080;
        return port > 0 && port <= 65535;
    }

    private static bool ValidateTcpClientConfig(Dictionary<string, object> config)
    {
        var host = config.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
        var port = config.GetValueOrDefault("Port", 8080) as int? ?? 8080;
        return !string.IsNullOrEmpty(host) && port > 0 && port <= 65535;
    }

    private static bool ValidateUdpServerConfig(Dictionary<string, object> config)
    {
        var port = config.GetValueOrDefault("Port", 8080) as int? ?? 8080;
        return port > 0 && port <= 65535;
    }

    private static bool ValidateUdpClientConfig(Dictionary<string, object> config)
    {
        var host = config.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
        var port = config.GetValueOrDefault("Port", 8080) as int? ?? 8080;
        return !string.IsNullOrEmpty(host) && port > 0 && port <= 65535;
    }

    private static bool ValidateWebSocketServerConfig(Dictionary<string, object> config)
    {
        var port = config.GetValueOrDefault("Port", 8080) as int? ?? 8080;
        var path = config.GetValueOrDefault("Path", "/") as string ?? "/";
        return port > 0 && port <= 65535 && !string.IsNullOrEmpty(path);
    }

    private static bool ValidateWebSocketClientConfig(Dictionary<string, object> config)
    {
        var host = config.GetValueOrDefault("Host", "localhost") as string ?? "localhost";
        var port = config.GetValueOrDefault("Port", 8080) as int? ?? 8080;
        return !string.IsNullOrEmpty(host) && port > 0 && port <= 65535;
    }

    private static bool ValidateSignalRServerConfig(Dictionary<string, object> config)
    {
        var port = config.GetValueOrDefault("Port", 5003) as int? ?? 5003;
        var hubPath = config.GetValueOrDefault("HubPath", "/messagingHub") as string ?? "/messagingHub";
        return port > 0 && port <= 65535 && !string.IsNullOrEmpty(hubPath);
    }

    private static bool ValidateSignalRClientConfig(Dictionary<string, object> config)
    {
        var serverUrl = config.GetValueOrDefault("ServerUrl") as string;
        return !string.IsNullOrEmpty(serverUrl) && Uri.TryCreate(serverUrl, UriKind.Absolute, out _);
    }

    private static bool ValidateGrpcServerConfig(Dictionary<string, object> config)
    {
        var port = config.GetValueOrDefault("Port", 5000) as int? ?? 5000;
        return port > 0 && port <= 65535;
    }

    private static bool ValidateGrpcClientConfig(Dictionary<string, object> config)
    {
        var serverAddress = config.GetValueOrDefault("ServerAddress") as string;
        return !string.IsNullOrEmpty(serverAddress) && Uri.TryCreate(serverAddress, UriKind.Absolute, out _);
    }

    private static bool ValidateNamedPipeServerConfig(Dictionary<string, object> config)
    {
        var pipeName = config.GetValueOrDefault("PipeName", "GenericMessagingApp") as string ?? "GenericMessagingApp";
        return !string.IsNullOrEmpty(pipeName);
    }

    private static bool ValidateNamedPipeClientConfig(Dictionary<string, object> config)
    {
        var pipeName = config.GetValueOrDefault("PipeName", "GenericMessagingApp") as string ?? "GenericMessagingApp";
        return !string.IsNullOrEmpty(pipeName);
    }

    private static bool ValidateRtpServerConfig(Dictionary<string, object> config)
    {
        var port = config.GetValueOrDefault("Port", 5004) as int? ?? 5004;
        return port > 0 && port <= 65535;
    }

    private static bool ValidateRtpClientConfig(Dictionary<string, object> config)
    {
        var serverAddress = config.GetValueOrDefault("ServerAddress") as string;
        var serverPort = config.GetValueOrDefault("ServerPort") as int?;
        return !string.IsNullOrEmpty(serverAddress) && serverPort.HasValue && serverPort > 0 && serverPort <= 65535;
    }
}