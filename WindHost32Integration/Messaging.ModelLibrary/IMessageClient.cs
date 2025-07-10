
namespace Messaging.ModelLibrary;

public interface IMessageClient : IDisposable
{
    event EventHandler<MessageEventArgs>? MessageReceived;
    event EventHandler<ConnectionEventArgs>? Connected;
    event EventHandler<ConnectionEventArgs>? Disconnected;
    event EventHandler<ErrorEventArgs>? ErrorOccurred;


    bool IsConnected { get; }
    ConnectionInfo? ConnectionInfo { get; }
    TransportType TransportType { get; }

    Task<bool> ConnectAsync(Dictionary<string, object> configuration);

    Task DisconnectAsync();
    Task<bool> SendMessageAsync(Message message);


}