using Messaging.ModelLibrary.Broker;

namespace Messaging.ModelLibrary.Abstract;

public interface IMessagingService : IDisposable
{
    event EventHandler<MessageEventArgs>? MessageReceived;
    event EventHandler<ConnectionEventArgs>? Connected;
    event EventHandler<ConnectionEventArgs>? Disconnected;
    event EventHandler<ErrorEventArgs>? ErrorOccurred;
    bool IsRunning { get; }
    MessagingMode Mode { get; }
    string ClientName { get; set; }
    IReadOnlyList<ConnectionInfo> Connections { get; }
    MessageBroker? Broker { get; }
    IReadOnlyList<ClientInfo> OnlineClients { get; }
    Task<bool> StartServerAsync(IMessageTransport transport, Dictionary<string, object>? configuration = null);
    Task<bool> ConnectAsClientAsync(IMessageClient client, Dictionary<string, object> configuration);
    Task<bool> SendDirectMessageAsync(string recipientId, string content, MessageType type = MessageType.Text);
    Task<bool> SendGroupMessageAsync(string groupName, string content);
    Task<IReadOnlyList<ClientInfo>> GetOnlineClientsAsync();
    Task<bool> CreateGroupAsync(string groupName);
    Task<bool> JoinGroupAsync(string groupName);
    Task<bool> LeaveGroupAsync(string groupName);
    Task StopAsync();
    Task<bool> SendMessageAsync(string content, string? receiver = null, MessageType type = MessageType.Text);
    Task<bool> SendMessageAsync(Message message);
    Task<bool> BroadcastMessageAsync(string content, MessageType type = MessageType.Text);
    void OnTransportMessageReceived(object? sender, MessageEventArgs e);
    void OnTransportClientConnected(object? sender, ConnectionEventArgs e);
    void OnTransportClientDisconnected(object? sender, ConnectionEventArgs e);
    void OnTransportErrorOccurred(object? sender, ErrorEventArgs e);
    void OnClientMessageReceived(object? sender, MessageEventArgs e);
    void OnClientConnected(object? sender, ConnectionEventArgs e);
    void OnClientDisconnected(object? sender, ConnectionEventArgs e);
    void OnClientErrorOccurred(object? sender, ErrorEventArgs e);
    void OnBrokerMessageRouted(object? sender, MessageEventArgs e);
    void OnBrokerClientDiscovered(object? sender, ClientDiscoveryEventArgs e);
    void OnBrokerClientDisconnected(object? sender, ClientDiscoveryEventArgs e);
    void OnBrokerErrorOccurred(object? sender, ErrorEventArgs e);
}