namespace Messaging.ModelLibrary.Helpers
{
    /// <summary>
    /// Helper class for message validation and processing
    /// </summary>
    public static class MessageProcessor
    {
        /// <summary>
        /// Validate message before routing
        /// </summary>
        public static bool ValidateMessage(Message message)
        {
            if (string.IsNullOrEmpty(message.Id))
                return false;

            if (string.IsNullOrEmpty(message.Sender))
                return false;

            if (string.IsNullOrEmpty(message.Receiver))
                return false;

            if (message.ExpiresIn.HasValue && message.Timestamp.Add(message.ExpiresIn.Value) < DateTime.UtcNow)
                return false; // Message expired

            return true;
        }

        /// <summary>
        /// Process message metadata
        /// </summary>
        public static void ProcessMetadata(Message message)
        {
            // Add processing timestamp
            message.Metadata["ProcessedAt"] = DateTime.UtcNow;

            // Add message size
            message.Metadata["ContentSize"] = message.Content.Length;

            // Add priority information
            message.Metadata["Priority"] = message.Priority.ToString();
        }

        /// <summary>
        /// Create acknowledgment message
        /// </summary>
        public static Message CreateAcknowledgment(Message originalMessage, bool success, string? reason = null)
        {
            return new Message
            {
                Content = success ? "ACK" : $"NACK: {reason}",
                Sender = "System",
                Receiver = originalMessage.Sender,
                Type = MessageType.Acknowledgment,
                ReplyToId = originalMessage.Id,
                Metadata = new Dictionary<string, object>
                {
                    ["OriginalMessageId"] = originalMessage.Id,
                    ["AcknowledgedAt"] = DateTime.UtcNow,
                    ["Success"] = success
                }
            };
        }

        /// <summary>
        /// Create error message
        /// </summary>
        public static Message CreateErrorMessage(string recipient, string error, string? originalMessageId = null)
        {
            return new Message
            {
                Content = error,
                Sender = "System",
                Receiver = recipient,
                Type = MessageType.Error,
                ReplyToId = originalMessageId,
                Metadata = new Dictionary<string, object>
                {
                    ["ErrorType"] = "SystemError",
                    ["ErrorTime"] = DateTime.UtcNow
                }
            };
        }
    }

    /// <summary>
    /// Configuration helper for client-to-client messaging
    /// </summary>
    public static class MessagingConfiguration
    {
        /// <summary>
        /// Create default gRPC client configuration
        /// </summary>
        public static Dictionary<string, object> CreateGrpcClientConfig(string serverAddress, string clientName)
        {
            return new Dictionary<string, object>
            {
                ["ServerAddress"] = serverAddress,
                ["ClientName"] = clientName,
                ["MaxReceiveMessageSize"] = 4 * 1024 * 1024,
                ["DisableCertificateValidation"] = true
            };
        }

        /// <summary>
        /// Create default Named Pipe client configuration
        /// </summary>
        public static Dictionary<string, object> CreateNamedPipeClientConfig(string pipeName, string clientName)
        {
            return new Dictionary<string, object>
            {
                ["PipeName"] = pipeName,
                ["ClientName"] = clientName,
                ["ServerName"] = ".",
                ["Timeout"] = 5000
            };
        }

        /// <summary>
        /// Create default RTP client configuration
        /// </summary>
        public static Dictionary<string, object> CreateRtpClientConfig(string serverAddress, int serverPort, string clientName)
        {
            return new Dictionary<string, object>
            {
                ["ServerAddress"] = serverAddress,
                ["ServerPort"] = serverPort,
                ["ClientName"] = clientName,
                ["LocalPort"] = 0,
                ["ReceiveTimeout"] = 5000,
                ["SendTimeout"] = 5000
            };
        }
    }
}
