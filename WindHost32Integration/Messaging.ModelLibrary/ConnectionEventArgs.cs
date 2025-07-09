namespace Messaging.ModelLibrary;

public class ConnectionEventArgs : EventArgs
{
    public ConnectionInfo Connection { get; }

    public ConnectionEventArgs(ConnectionInfo connection)
    {
        Connection = connection;
    }
}