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

    Task<bool> StartServerAsync(IMessageTransport transport, Dictionary<string, object>? configuration = null);
    Task<bool> ConnectAsClientAsync(IMessageClient client, Dictionary<string, object> configuration);
    Task StopAsync();
    Task<bool> SendMessageAsync(string content, string? receiver = null, MessageType type = MessageType.Text);
    Task<bool> SendMessageAsync(Message message);
    Task<bool> BroadcastMessageAsync(string content, MessageType type = MessageType.Text);
}