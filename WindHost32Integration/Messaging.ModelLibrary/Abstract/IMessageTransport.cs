namespace Messaging.ModelLibrary.Abstract;

public interface IMessageTransport : IDisposable
{
    event EventHandler<MessageEventArgs>? MessageReceived;
    event EventHandler<ConnectionEventArgs>? ClientConnected;
    event EventHandler<ConnectionEventArgs>? ClientDisconnected;
    event EventHandler<ErrorEventArgs>? ErrorOccurred;

    bool IsRunning { get; }
    TransportType TransportType { get; }
    IReadOnlyList<ConnectionInfo> Connections { get; }

    Task<bool> StartAsync(Dictionary<string, object>? configuration = null);
    Task StopAsync();
    Task<bool> SendMessageAsync(Message message, string? connectionId = null);
    Task<bool> BroadcastMessageAsync(Message message);
}