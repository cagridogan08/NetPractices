using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Messaging.ModelLibrary.ServerSentEvents
{
    /// <summary>
    /// Configuration helper for SSE
    /// </summary>
    internal class SseConfig
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 8080;
        public string EventsPath { get; set; } = "/events";
        public string MessagesPath { get; set; } = "/messages";
        public string ClientName { get; set; } = Environment.UserName;

        public static SseConfig FromDictionary(Dictionary<string, object>? configuration)
        {
            if (configuration == null) return new SseConfig();

            return new SseConfig
            {
                Host = configuration.GetValueOrDefault("Host", "localhost") as string ?? "localhost",
                Port = configuration.GetValueOrDefault("Port", 8080) as int? ?? 8080,
                EventsPath = configuration.GetValueOrDefault("EventsPath", "/events") as string ?? "/events",
                MessagesPath = configuration.GetValueOrDefault("MessagesPath", "/messages") as string ?? "/messages",
                ClientName = configuration.GetValueOrDefault("ClientName", Environment.UserName) as string ?? Environment.UserName
            };
        }
    }
}
