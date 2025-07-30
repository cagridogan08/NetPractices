namespace Messaging.ModelLibrary;

public enum TransportType
{
    NamedPipe,
    Tcp,
    Udp,
    SignalR,
    WebSocket,
    InMemory,
    gRPC,
    RabbitMQ,
    Rtp,
    Mqtt,
    ZeroMQ,
    Redis,
    ServerSentEvents,
}