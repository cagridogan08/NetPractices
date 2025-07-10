
namespace Messaging.ModelLibrary;

/// <summary>
/// Enhanced base class for message clients with built-in client-to-client features
/// </summary>
public abstract class MessageClientBase : IMessageClient
{
    #region Fields
    protected IClientDiscovery? _clientDiscovery;
    protected IMessageStore? _messageStore;
    protected IGroupManager? _groupManager;
    protected readonly Dictionary<string, TaskCompletionSource<bool>> _pendingAcknowledgments = new();

    #endregion

    #region Properties
    public abstract bool IsConnected { get; }
    public abstract ConnectionInfo? ConnectionInfo { get; protected set; }
    public abstract TransportType TransportType { get; }
    #endregion

    #region Events
    public event EventHandler<MessageEventArgs>? MessageReceived;
    public event EventHandler<ConnectionEventArgs>? Connected;
    public event EventHandler<ConnectionEventArgs>? Disconnected;
    public event EventHandler<ErrorEventArgs>? ErrorOccurred;
    public event EventHandler<ClientDiscoveryEventArgs>? ClientDiscovered;
    public event EventHandler<ClientDiscoveryEventArgs>? ClientDisconnected;
    #endregion

    #region Abstract Methods
    public abstract Task<bool> ConnectAsync(Dictionary<string, object> configuration);
    public abstract Task DisconnectAsync();
    public abstract Task<bool> SendMessageAsync(Message message);
    #endregion

    #region Enhanced Messaging Methods
    public virtual async Task<bool> SendDirectMessageAsync(string recipientId, string content, MessageType type = MessageType.Text)
    {
        var message = new Message
        {
            Content = content,
            Receiver = recipientId,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Type = type
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> SendDirectMessageAsync(string recipientId, Message message)
    {
        message.Receiver = recipientId;
        message.Sender = ConnectionInfo?.Name ?? "Unknown";
        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> BroadcastMessageAsync(string content, MessageType type = MessageType.Text)
    {
        var message = new Message
        {
            Content = content,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Type = type
        };

        return await BroadcastMessageAsync(message);
    }

    public virtual async Task<bool> BroadcastMessageAsync(Message message)
    {
        message.Sender = ConnectionInfo?.Name ?? "Unknown";
        message.Receiver = "*"; // Broadcast indicator
        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> ReplyToMessageAsync(string originalMessageId, string content)
    {
        var message = new Message
        {
            Content = content,
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Type = MessageType.Text,
            ReplyToId = originalMessageId
        };

        return await SendMessageAsync(message);
    }

    public virtual async Task<bool> SendAcknowledgmentAsync(string messageId, bool success = true, string? reason = null)
    {
        var message = new Message
        {
            Content = success ? "ACK" : $"NACK: {reason}",
            Sender = ConnectionInfo?.Name ?? "Unknown",
            Type = MessageType.Acknowledgment,
            ReplyToId = messageId
        };

        return await SendMessageAsync(message);
    }
    #endregion

    #region Client Discovery
    public virtual async Task<IReadOnlyList<ClientInfo>> GetOnlineClientsAsync()
    {
        if (_clientDiscovery != null)
            return await _clientDiscovery.GetOnlineClientsAsync();

        return new List<ClientInfo>();
    }

    public virtual async Task<ClientInfo?> GetClientInfoAsync(string clientId)
    {
        if (_clientDiscovery != null)
            return await _clientDiscovery.GetClientAsync(clientId);

        return null;
    }

    public virtual async Task<bool> IsClientOnlineAsync(string clientId)
    {
        if (_clientDiscovery != null)
            return await _clientDiscovery.IsClientOnlineAsync(clientId);

        return false;
    }
    #endregion

    #region Group Management
    public virtual async Task<bool> JoinGroupAsync(string groupName)
    {
        if (_groupManager != null && ConnectionInfo != null)
            return await _groupManager.JoinGroupAsync(groupName, ConnectionInfo.Id);

        return false;
    }

    public virtual async Task<bool> LeaveGroupAsync(string groupName)
    {
        if (_groupManager != null && ConnectionInfo != null)
            return await _groupManager.LeaveGroupAsync(groupName, ConnectionInfo.Id);

        return false;
    }

    public virtual async Task<bool> SendGroupMessageAsync(string groupName, string content)
    {
        if (_groupManager != null)
        {
            var message = new Message
            {
                Content = content,
                Sender = ConnectionInfo?.Name ?? "Unknown",
                Receiver = $"group:{groupName}",
                Type = MessageType.Text
            };

            return await _groupManager.SendGroupMessageAsync(groupName, message);
        }

        return false;
    }

    public virtual async Task<IReadOnlyList<string>> GetJoinedGroupsAsync()
    {
        if (_groupManager != null && ConnectionInfo != null)
            return await _groupManager.GetClientGroupsAsync(ConnectionInfo.Id);

        return new List<string>();
    }
    #endregion

    #region Event Handlers
    protected virtual void OnMessageReceived(MessageEventArgs e)
    {
        // Handle acknowledgments
        if (e.Message.Type == MessageType.Acknowledgment && !string.IsNullOrEmpty(e.Message.ReplyToId))
        {
            if (_pendingAcknowledgments.Remove(e.Message.ReplyToId, out var tcs))
            {
                tcs.SetResult(e.Message.Content == "ACK");
            }
        }

        MessageReceived?.Invoke(this, e);
    }

    protected virtual void OnConnected(ConnectionEventArgs e) => Connected?.Invoke(this, e);
    protected virtual void OnDisconnected(ConnectionEventArgs e) => Disconnected?.Invoke(this, e);
    protected virtual void OnErrorOccurred(ErrorEventArgs e) => ErrorOccurred?.Invoke(this, e);
    protected virtual void OnClientDiscovered(ClientDiscoveryEventArgs e) => ClientDiscovered?.Invoke(this, e);
    protected virtual void OnClientDisconnected(ClientDiscoveryEventArgs e) => ClientDisconnected?.Invoke(this, e);
    #endregion

    #region Disposal
    public abstract void Dispose();
    #endregion
}