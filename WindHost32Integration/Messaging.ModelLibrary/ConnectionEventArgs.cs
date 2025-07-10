namespace Messaging.ModelLibrary;

public class ConnectionEventArgs(ConnectionInfo connection) : EventArgs
{
    public ConnectionInfo Connection { get; } = connection;
}