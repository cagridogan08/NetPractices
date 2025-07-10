namespace Messaging.ModelLibrary;

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Receiver { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public MessageType Type { get; set; } = MessageType.Text;
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string? ReplyToId { get; set; }
    public TimeSpan? ExpiresIn { get; set; }
    public bool RequiresAcknowledgment { get; set; }
    public string[]? Tags { get; set; }
}