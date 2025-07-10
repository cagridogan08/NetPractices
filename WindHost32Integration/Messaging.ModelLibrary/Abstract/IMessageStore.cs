
namespace Messaging.ModelLibrary.Abstract
{

    /// <summary>
    /// Interface for message routing and delivery
    /// </summary>
    public interface IMessageRouter
    {
        Task<bool> RouteMessageAsync(Message message, IMessageTransport transport);
        Task<bool> CanRouteToClientAsync(string clientId, IMessageTransport transport);
        void RegisterTransport(IMessageTransport transport);
        void UnregisterTransport(IMessageTransport transport);
    }

    /// <summary>
    /// Interface for client discovery and presence management
    /// </summary>
    public interface IClientDiscovery
    {
        Task<IReadOnlyList<ClientInfo>> GetOnlineClientsAsync();
        Task<ClientInfo?> GetClientAsync(string clientId);
        Task<bool> IsClientOnlineAsync(string clientId);
        Task RegisterClientAsync(ClientInfo client);
        Task UnregisterClientAsync(string clientId);
        Task UpdateClientPresenceAsync(string clientId, DateTime lastSeen);

        event EventHandler<ClientDiscoveryEventArgs>? ClientDiscovered;
        event EventHandler<ClientDiscoveryEventArgs>? ClientDisconnected;
    }
    /// <summary>
    /// Interface for message persistence and history
    /// </summary>
    public interface IMessageStore
    {
        Task<bool> StoreMessageAsync(Message message);
        Task<IReadOnlyList<Message>> GetMessagesAsync(string senderId, string receiverId, int limit = 100);
        Task<IReadOnlyList<Message>> GetConversationAsync(string clientId1, string clientId2, int limit = 100);
        Task<Message?> GetMessageAsync(string messageId);
        Task<bool> MarkMessageAsDeliveredAsync(string messageId);
        Task<bool> MarkMessageAsReadAsync(string messageId);
        Task<bool> DeleteMessageAsync(string messageId);
    }

    /// <summary>
    /// Interface for group/channel management
    /// </summary>
    public interface IGroupManager
    {
        Task<bool> CreateGroupAsync(string groupName, string creatorId);
        Task<bool> DeleteGroupAsync(string groupName);
        Task<bool> JoinGroupAsync(string groupName, string clientId);
        Task<bool> LeaveGroupAsync(string groupName, string clientId);
        Task<IReadOnlyList<string>> GetGroupMembersAsync(string groupName);
        Task<IReadOnlyList<string>> GetClientGroupsAsync(string clientId);
        Task<bool> SendGroupMessageAsync(string groupName, Message message);
    }
}
