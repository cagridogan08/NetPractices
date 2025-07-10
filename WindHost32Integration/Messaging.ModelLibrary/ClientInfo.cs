namespace Messaging.ModelLibrary;

public class ClientInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public TransportType TransportType { get; set; }
    public string Address { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public bool IsOnline { get; set; } = true;
    public Dictionary<string, object> Properties { get; set; } = new();
    public string[]? Groups { get; set; }
    public string[]? Tags { get; set; }
}