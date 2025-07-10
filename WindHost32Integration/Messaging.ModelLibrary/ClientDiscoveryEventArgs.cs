namespace Messaging.ModelLibrary;

public class ClientDiscoveryEventArgs : EventArgs
{
    public ClientInfo Client { get; }
    public bool IsOnline { get; }

    public ClientDiscoveryEventArgs(ClientInfo client, bool isOnline)
    {
        Client = client;
        IsOnline = isOnline;
    }
}