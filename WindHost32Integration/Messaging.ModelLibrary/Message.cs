namespace Messaging.ModelLibrary;

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Receiver { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public MessageType Type { get; set; } = MessageType.Text;
    public Dictionary<string, object> Metadata { get; set; } = new();
}