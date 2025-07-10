namespace Messaging.ModelLibrary;

public class MessageEventArgs(Message message, ConnectionInfo connection = null) : EventArgs
{
    public Message Message { get; } = message;
    public ConnectionInfo Connection { get; } = connection;
}