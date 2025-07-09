namespace Messaging.ModelLibrary;

public class MessageEventArgs : EventArgs
{
    public Message Message { get; }
    public ConnectionInfo Connection { get; }

    public MessageEventArgs(Message message, ConnectionInfo connection = null)
    {
        Message = message;
        Connection = connection;
    }
}