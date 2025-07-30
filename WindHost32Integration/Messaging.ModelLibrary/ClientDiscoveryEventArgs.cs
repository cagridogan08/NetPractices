namespace Messaging.ModelLibrary;

public class ClientDiscoveryEventArgs(ClientInfo client, bool isOnline) : EventArgs
{
    public ClientInfo Client { get; } = client;
    public bool IsOnline { get; } = isOnline;
}