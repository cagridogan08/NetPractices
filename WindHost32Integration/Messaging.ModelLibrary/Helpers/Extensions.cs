using Messaging.ModelLibrary.Abstract;
using Messaging.ModelLibrary.Grpc;
using Messaging.ModelLibrary.Pipe;
using Messaging.ModelLibrary.RTP;
using Messaging.ModelLibrary.SignalR;
using Messaging.ModelLibrary.Tcp;
using Messaging.ModelLibrary.Udp;
using Messaging.ModelLibrary.WebSocket;

namespace Messaging.ModelLibrary.Helpers
{
    /// <summary>
    /// Factory for creating pre-configured messaging services
    /// </summary>
    public static class MessagingServiceFactory
    {
        public static MessagingServiceBuilder CreateBuilder() => new MessagingServiceBuilder();

        public static async Task<MessagingService> CreateTcpServerAsync(int port = 8080, string host = "localhost")
        {
            return await CreateBuilder()
                .UseTransport(TransportType.Tcp)
                .AsServer()
                .OnHost(host)
                .OnPort(port)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateTcpClientAsync(string host = "localhost", int port = 8080, string clientName = null)
        {
            return await CreateBuilder()
                .UseTransport(TransportType.Tcp)
                .AsClient()
                .OnHost(host)
                .OnPort(port)
                .WithClientName(clientName ?? Environment.UserName)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateWebSocketServerAsync(int port = 8080, string host = "localhost", string path = "/")
        {
            return await CreateBuilder()
                .UseTransport(TransportType.WebSocket)
                .AsServer()
                .OnHost(host)
                .OnPort(port)
                .WithConfiguration("Path", path)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateWebSocketClientAsync(string host = "localhost", int port = 8080, string path = "/", string clientName = null)
        {
            return await CreateBuilder()
                .UseTransport(TransportType.WebSocket)
                .AsClient()
                .OnHost(host)
                .OnPort(port)
                .WithConfiguration("Path", path)
                .WithClientName(clientName ?? Environment.UserName)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateSignalRServerAsync(int port = 5003, string hubPath = "/messagingHub")
        {
            return await CreateBuilder()
                .UseTransport(TransportType.SignalR)
                .AsServer()
                .OnPort(port)
                .WithConfiguration("HubPath", hubPath)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateSignalRClientAsync(string serverUrl, string clientName = null)
        {
            return await CreateBuilder()
                .UseTransport(TransportType.SignalR)
                .AsClient()
                .WithConfiguration("ServerUrl", serverUrl)
                .WithClientName(clientName ?? Environment.UserName)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateNamedPipeServerAsync(string pipeName = "GenericMessagingApp")
        {
            return await CreateBuilder()
                .UseTransport(TransportType.NamedPipe)
                .AsServer()
                .WithConfiguration("PipeName", pipeName)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateNamedPipeClientAsync(string pipeName = "GenericMessagingApp", string clientName = null)
        {
            return await CreateBuilder()
                .UseTransport(TransportType.NamedPipe)
                .AsClient()
                .WithConfiguration("PipeName", pipeName)
                .WithClientName(clientName ?? Environment.UserName)
                .BuildAsync();
        }

        public static async Task<MessagingService> CreateMultiTransportServerAsync(params TransportType[] transportTypes)
        {
            if (transportTypes == null || transportTypes.Length == 0)
                throw new ArgumentException("At least one transport type must be specified");

            // For multi-transport, we'll create the first transport and manually add others
            var primaryTransport = transportTypes[0];
            var service = await CreateBuilder()
                .UseTransport(primaryTransport)
                .AsServer()
                .BuildAsync();

            // Add additional transports to the broker
            if (service.Broker != null)
            {
                foreach (var transportType in transportTypes.Skip(1))
                {
                    var transport = CreateTransportInstance(transportType);
                    service.Broker.RegisterTransport(transport);

                    // Start the transport with default configuration
                    var config = GetDefaultServerConfig(transportType);
                    await transport.StartAsync(config);
                }
            }

            return service;
        }

        private static IMessageTransport CreateTransportInstance(TransportType transportType)
        {
            return transportType switch
            {
                TransportType.Tcp => new TcpTransport(),
                TransportType.Udp => new UdpTransport(),
                TransportType.WebSocket => new WebSocketTransport(),
                TransportType.NamedPipe => new NamedPipeTransport(),
                TransportType.SignalR => new SignalRTransport(),
                TransportType.gRPC => new GrpcTransport(),
                TransportType.Rtp => new RtpTransport(),
                _ => throw new NotSupportedException($"Transport type {transportType} is not supported")
            };
        }

        private static Dictionary<string, object> GetDefaultServerConfig(TransportType transportType)
        {
            return transportType switch
            {
                TransportType.Tcp => MessagingConfigurationFactory.CreateTcpServerConfig(port: 8081),
                TransportType.Udp => MessagingConfigurationFactory.CreateUdpServerConfig(port: 8082),
                TransportType.WebSocket => MessagingConfigurationFactory.CreateWebSocketServerConfig(port: 8083),
                TransportType.NamedPipe => MessagingConfigurationFactory.CreateNamedPipeServerConfig("MultiTransportPipe"),
                TransportType.SignalR => MessagingConfigurationFactory.CreateSignalRServerConfig(port: 5004),
                TransportType.gRPC => MessagingConfigurationFactory.CreateGrpcServerConfig(port: 5001),
                TransportType.Rtp => MessagingConfigurationFactory.CreateRtpServerConfig(port: 5005),
                _ => new Dictionary<string, object>()
            };
        }
    }
}
