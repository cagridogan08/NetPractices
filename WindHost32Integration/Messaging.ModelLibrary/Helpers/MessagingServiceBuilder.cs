using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Grpc;
using Messaging.ModelLibrary.Pipe;
using Messaging.ModelLibrary.RTP;
using Messaging.ModelLibrary.SignalR;
using Messaging.ModelLibrary.Tcp;
using Messaging.ModelLibrary.Udp;
using Messaging.ModelLibrary.WebSocket;

namespace Messaging.ModelLibrary.Helpers;

/// <summary>
/// Builder pattern for creating messaging services with different configurations
/// </summary>
public class MessagingServiceBuilder
{
    private TransportType _transportType = TransportType.Tcp;
    private MessagingMode _mode = MessagingMode.Server;
    private Dictionary<string, object> _configuration = new();
    private IMessageStore? _messageStore;
    private string _clientName = Environment.UserName;
    private bool _enableBroker = true;

    public MessagingServiceBuilder UseTransport(TransportType transportType)
    {
        _transportType = transportType;
        return this;
    }

    public MessagingServiceBuilder AsServer()
    {
        _mode = MessagingMode.Server;
        return this;
    }

    public MessagingServiceBuilder AsClient()
    {
        _mode = MessagingMode.Client;
        return this;
    }

    public MessagingServiceBuilder WithConfiguration(Dictionary<string, object> configuration)
    {
        _configuration = configuration;
        return this;
    }

    public MessagingServiceBuilder WithConfiguration(string key, object value)
    {
        _configuration[key] = value;
        return this;
    }

    public MessagingServiceBuilder WithMessageStore(IMessageStore messageStore)
    {
        _messageStore = messageStore;
        return this;
    }

    public MessagingServiceBuilder WithClientName(string clientName)
    {
        _clientName = clientName;
        return this;
    }

    public MessagingServiceBuilder EnableBroker(bool enable = true)
    {
        _enableBroker = enable;
        return this;
    }

    public MessagingServiceBuilder OnPort(int port)
    {
        _configuration["Port"] = port;
        return this;
    }

    public MessagingServiceBuilder OnHost(string host)
    {
        _configuration["Host"] = host;
        return this;
    }

    public MessagingServiceBuilder WithTimeout(int timeoutMs)
    {
        _configuration["Timeout"] = timeoutMs;
        return this;
    }

    public async Task<MessagingService> BuildAsync()
    {
        var service = new MessagingService();
        service.ClientName = _clientName;

        if (_mode == MessagingMode.Server)
        {
            var transport = CreateTransport();
            await service.StartServerAsync(transport, _configuration);
        }
        else
        {
            var client = CreateClient();
            _configuration.TryAdd("ClientName", _clientName);

            await service.ConnectAsClientAsync(client, _configuration);
        }

        return service;
    }

    private IMessageTransport CreateTransport()
    {
        return _transportType switch
        {
            TransportType.Tcp => new TcpTransport(),
            TransportType.Udp => new UdpTransport(),
            TransportType.WebSocket => new WebSocketTransport(),
            TransportType.NamedPipe => new NamedPipeTransport(_configuration.GetValueOrDefault("PipeName", "GenericMessagingApp") as string ?? "GenericMessagingApp"),
            TransportType.SignalR => new SignalRTransport(
                _configuration.GetValueOrDefault("Address", "localhost") as string ?? "localhost",
                _configuration.GetValueOrDefault("Port", 5003) as int? ?? 5003,
                _configuration.GetValueOrDefault("HubPath", "/messagingHub") as string ?? "/messagingHub"),
            TransportType.gRPC => new GrpcTransport(
                _configuration.GetValueOrDefault("Address", "localhost") as string ?? "localhost",
                _configuration.GetValueOrDefault("Port", 5000) as int? ?? 5000),
            TransportType.Rtp => new RtpTransport(
                _configuration.GetValueOrDefault("Port", 5004) as int? ?? 5004),
            _ => throw new NotSupportedException($"Transport type {_transportType} is not supported")
        };
    }

    private IMessageClient CreateClient()
    {
        return _transportType switch
        {
            TransportType.Tcp => new TcpMessageClient(),
            TransportType.Udp => new UdpMessageClient(),
            TransportType.WebSocket => new WebSocketMessageClient(),
            TransportType.NamedPipe => new NamedPipeClient(),
            TransportType.SignalR => new SignalRClient(),
            TransportType.gRPC => new GrpcClient(),
            TransportType.Rtp => new RtpClient(),
            _ => throw new NotSupportedException($"Transport type {_transportType} is not supported")
        };
    }
}