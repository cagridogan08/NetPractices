using System.Collections.Concurrent;
using Messaging.ModelLibrary;
using Messaging.ModelLibrary.InMemory;
using Messaging.ModelLibrary.Tcp;
using Messaging.ModelLibrary.Udp;
using Messaging.ModelLibrary.WebSocket;

namespace Messsaging.Tests;

[TestClass]
public class TcpTransportTests
{
    private const int TestPort = 9001;

    [TestMethod]
    public async Task TcpTransport_StartStop_Success()
    {
        // Arrange
        var transport = new TcpTransport();
        var config = new Dictionary<string, object> { ["Port"] = TestPort };

        // Act
        var startResult = await transport.StartAsync(config);

        // Assert
        Assert.IsTrue(startResult);
        Assert.IsTrue(transport.IsRunning);

        // Cleanup
        await transport.StopAsync();
        Assert.IsFalse(transport.IsRunning);
    }

    [TestMethod]
    public async Task TcpTransport_ClientConnectDisconnect_Success()
    {
        // Arrange
        var transport = new TcpTransport();
        var client = new TcpMessageClient();
        var config = new Dictionary<string, object> { ["Port"] = TestPort };

        var clientConnected = false;
        var clientDisconnected = false;

        transport.ClientConnected += (s, e) => clientConnected = true;
        transport.ClientDisconnected += (s, e) => clientDisconnected = true;

        // Act
        await transport.StartAsync(config);

        var connectResult = await client.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["ClientName"] = "TestClient"
        });

        await Task.Delay(100); // Allow event processing

        await client.DisconnectAsync();
        await Task.Delay(100); // Allow event processing

        // Assert
        Assert.IsTrue(connectResult);
        Assert.IsTrue(clientConnected);
        Assert.IsTrue(clientDisconnected);

        // Cleanup
        await transport.StopAsync();
    }

    [TestMethod]
    public async Task TcpTransport_SendReceiveMessage_Success()
    {
        // Arrange
        var transport = new TcpTransport();
        var client = new TcpMessageClient();
        var config = new Dictionary<string, object> { ["Port"] = TestPort };

        Message receivedMessage = null;
        transport.MessageReceived += (s, e) => receivedMessage = e.Message;

        // Act
        await transport.StartAsync(config);
        await client.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["ClientName"] = "TestClient"
        });

        await Task.Delay(100); // Allow connection to establish

        var testMessage = new Message
        {
            Content = "Hello TCP!",
            Sender = "TestClient",
            Type = MessageType.Text
        };

        await client.SendMessageAsync(testMessage);
        await Task.Delay(100); // Allow message processing

        // Assert
        Assert.IsNotNull(receivedMessage);
        Assert.AreEqual("Hello TCP!", receivedMessage.Content);
        Assert.AreEqual("TestClient", receivedMessage.Sender);

        // Cleanup
        await client.DisconnectAsync();
        await transport.StopAsync();
    }

    [TestMethod]
    public async Task TcpTransport_BroadcastMessage_Success()
    {
        // Arrange
        var transport = new TcpTransport();
        var client1 = new TcpMessageClient();
        var client2 = new TcpMessageClient();
        var config = new Dictionary<string, object> { ["Port"] = TestPort };

        var receivedMessages = new ConcurrentBag<Message>();

        client1.MessageReceived += (s, e) => receivedMessages.Add(e.Message);
        client2.MessageReceived += (s, e) => receivedMessages.Add(e.Message);

        // Act
        await transport.StartAsync(config);

        await client1.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["ClientName"] = "Client1"
        });

        await client2.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["ClientName"] = "Client2"
        });

        await Task.Delay(100); // Allow connections to establish

        var broadcastMessage = new Message
        {
            Content = "Broadcast message!",
            Sender = "Server",
            Type = MessageType.Text
        };

        await transport.BroadcastMessageAsync(broadcastMessage);
        await Task.Delay(100); // Allow message processing

        // Assert
        Assert.AreEqual(2, receivedMessages.Count);
        Assert.IsTrue(receivedMessages.All(m => m.Content == "Broadcast message!"));

        // Cleanup
        await client1.DisconnectAsync();
        await client2.DisconnectAsync();
        await transport.StopAsync();
    }
}

[TestClass]
public class UdpTransportTests
{
    private const int TestPort = 9002;

    [TestMethod]
    public async Task UdpTransport_StartStop_Success()
    {
        // Arrange
        var transport = new UdpTransport();
        var config = new Dictionary<string, object> { ["Port"] = TestPort };

        // Act
        var startResult = await transport.StartAsync(config);

        // Assert
        Assert.IsTrue(startResult);
        Assert.IsTrue(transport.IsRunning);

        // Cleanup
        await transport.StopAsync();
        Assert.IsFalse(transport.IsRunning);
    }

    [TestMethod]
    public async Task UdpTransport_ClientConnectDisconnect_Success()
    {
        // Arrange
        var transport = new UdpTransport();
        var client = new UdpMessageClient();
        var config = new Dictionary<string, object> { ["Port"] = TestPort };

        var clientConnected = false;
        var clientDisconnected = false;

        transport.ClientConnected += (s, e) => clientConnected = true;
        transport.ClientDisconnected += (s, e) => clientDisconnected = true;

        // Act
        await transport.StartAsync(config);

        var connectResult = await client.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["ClientName"] = "TestClient"
        });

        await Task.Delay(200); // Allow event processing

        await client.DisconnectAsync();
        await Task.Delay(200); // Allow event processing

        // Assert
        Assert.IsTrue(connectResult);
        Assert.IsTrue(clientConnected);
        Assert.IsTrue(clientDisconnected);

        // Cleanup
        await transport.StopAsync();
    }

    [TestMethod]
    public async Task UdpTransport_SendReceiveMessage_Success()
    {
        // Arrange
        var transport = new UdpTransport();
        var client = new UdpMessageClient();
        var config = new Dictionary<string, object> { ["Port"] = TestPort };

        Message receivedMessage = null;
        transport.MessageReceived += (s, e) => receivedMessage = e.Message;

        // Act
        await transport.StartAsync(config);
        await client.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["ClientName"] = "TestClient"
        });

        await Task.Delay(200); // Allow connection to establish

        var testMessage = new Message
        {
            Content = "Hello UDP!",
            Sender = "TestClient",
            Type = MessageType.Text
        };

        await client.SendMessageAsync(testMessage);
        await Task.Delay(200); // Allow message processing

        // Assert
        Assert.IsNotNull(receivedMessage);
        Assert.AreEqual("Hello UDP!", receivedMessage.Content);
        Assert.AreEqual("TestClient", receivedMessage.Sender);

        // Cleanup
        await client.DisconnectAsync();
        await transport.StopAsync();
    }
}

[TestClass]
public class WebSocketTransportTests
{
    private const int TestPort = 9003;

    [TestMethod]
    public async Task WebSocketTransport_StartStop_Success()
    {
        // Arrange
        var transport = new WebSocketTransport();
        var config = new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["Path"] = "/test/"
        };

        // Act
        var startResult = await transport.StartAsync(config);

        // Assert
        Assert.IsTrue(startResult);
        Assert.IsTrue(transport.IsRunning);

        // Cleanup
        await transport.StopAsync();
        Assert.IsFalse(transport.IsRunning);
    }

    [TestMethod]
    public async Task WebSocketTransport_ClientConnectDisconnect_Success()
    {
        // Arrange
        var transport = new WebSocketTransport();
        var client = new WebSocketMessageClient();
        var config = new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["Path"] = "/test/"
        };

        var clientConnected = false;
        var clientDisconnected = false;

        transport.ClientConnected += (s, e) => clientConnected = true;
        transport.ClientDisconnected += (s, e) => clientDisconnected = true;

        // Act
        await transport.StartAsync(config);

        var connectResult = await client.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["Path"] = "/test/",
            ["ClientName"] = "TestClient"
        });

        await Task.Delay(200); // Allow event processing

        await client.DisconnectAsync();
        await Task.Delay(200); // Allow event processing

        // Assert
        Assert.IsTrue(connectResult);
        Assert.IsTrue(clientConnected);
        Assert.IsTrue(clientDisconnected);

        // Cleanup
        await transport.StopAsync();
    }

    [TestMethod]
    public async Task WebSocketTransport_SendReceiveMessage_Success()
    {
        // Arrange
        var transport = new WebSocketTransport();
        var client = new WebSocketMessageClient();
        var config = new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["Path"] = "/test/"
        };

        Message receivedMessage = null;
        transport.MessageReceived += (s, e) => receivedMessage = e.Message;

        // Act
        await transport.StartAsync(config);
        await client.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = TestPort,
            ["Path"] = "/test/",
            ["ClientName"] = "TestClient"
        });

        await Task.Delay(200); // Allow connection to establish

        var testMessage = new Message
        {
            Content = "Hello WebSocket!",
            Sender = "TestClient",
            Type = MessageType.Text
        };

        await client.SendMessageAsync(testMessage);
        await Task.Delay(200); // Allow message processing

        // Assert
        Assert.IsNotNull(receivedMessage);
        Assert.AreEqual("Hello WebSocket!", receivedMessage.Content);
        Assert.AreEqual("TestClient", receivedMessage.Sender);

        // Cleanup
        await client.DisconnectAsync();
        await transport.StopAsync();
    }
}

[TestClass]
public class InMemoryTransportTests
{
    [TestMethod]
    public async Task InMemoryTransport_StartStop_Success()
    {
        // Arrange
        var transport = new InMemoryTransport("test-server");

        // Act
        var startResult = await transport.StartAsync();

        // Assert
        Assert.IsTrue(startResult);
        Assert.IsTrue(transport.IsRunning);

        // Cleanup
        await transport.StopAsync();
        Assert.IsFalse(transport.IsRunning);
    }

    [TestMethod]
    public async Task InMemoryTransport_ClientConnectDisconnect_Success()
    {
        // Arrange
        var transport = new InMemoryTransport("test-server");
        var client = new InMemoryClient("TestClient");

        var clientConnected = false;
        var clientDisconnected = false;

        transport.ClientConnected += (s, e) => clientConnected = true;
        transport.ClientDisconnected += (s, e) => clientDisconnected = true;

        // Act
        await transport.StartAsync();

        var connectResult = await client.ConnectAsync(new Dictionary<string, object>
        {
            ["ServerName"] = "test-server"
        });

        await Task.Delay(100); // Allow event processing

        await client.DisconnectAsync();
        await Task.Delay(100); // Allow event processing

        // Assert
        Assert.IsTrue(connectResult);
        Assert.IsTrue(clientConnected);
        Assert.IsTrue(clientDisconnected);

        // Cleanup
        await transport.StopAsync();
    }

    [TestMethod]
    public async Task InMemoryTransport_SendReceiveMessage_Success()
    {
        // Arrange
        var transport = new InMemoryTransport("test-server");
        var client = new InMemoryClient("TestClient");

        Message receivedMessage = null;
        transport.MessageReceived += (s, e) => receivedMessage = e.Message;

        // Act
        await transport.StartAsync();
        await client.ConnectAsync(new Dictionary<string, object>
        {
            ["ServerName"] = "test-server"
        });

        await Task.Delay(100); // Allow connection to establish

        var testMessage = new Message
        {
            Content = "Hello InMemory!",
            Sender = "TestClient",
            Type = MessageType.Text
        };

        await client.SendMessageAsync(testMessage);
        await Task.Delay(100); // Allow message processing

        // Assert
        Assert.IsNotNull(receivedMessage);
        Assert.AreEqual("Hello InMemory!", receivedMessage.Content);
        Assert.AreEqual("TestClient", receivedMessage.Sender);

        // Cleanup
        await client.DisconnectAsync();
        await transport.StopAsync();
    }

    [TestMethod]
    public async Task InMemoryTransport_BroadcastMessage_Success()
    {
        // Arrange
        var transport = new InMemoryTransport("test-server");
        var client1 = new InMemoryClient("Client1");
        var client2 = new InMemoryClient("Client2");

        var receivedMessages = new ConcurrentBag<Message>();

        client1.MessageReceived += (s, e) => receivedMessages.Add(e.Message);
        client2.MessageReceived += (s, e) => receivedMessages.Add(e.Message);

        // Act
        await transport.StartAsync();

        await client1.ConnectAsync(new Dictionary<string, object>
        {
            ["ServerName"] = "test-server"
        });

        await client2.ConnectAsync(new Dictionary<string, object>
        {
            ["ServerName"] = "test-server"
        });

        await Task.Delay(100); // Allow connections to establish

        var broadcastMessage = new Message
        {
            Content = "Broadcast message!",
            Sender = "Server",
            Type = MessageType.Text
        };

        await transport.BroadcastMessageAsync(broadcastMessage);
        await Task.Delay(100); // Allow message processing

        // Assert
        Assert.AreEqual(2, receivedMessages.Count);
        Assert.IsTrue(receivedMessages.All(m => m.Content == "Broadcast message!"));

        // Cleanup
        await client1.DisconnectAsync();
        await client2.DisconnectAsync();
        await transport.StopAsync();
    }
}

[TestClass]
public class MessagingServiceTests
{
    [TestMethod]
    public async Task MessagingService_ServerClientCommunication_Success()
    {
        // Arrange
        var serverService = new MessagingService();
        var clientService = new MessagingService();

        var serverTransport = new InMemoryTransport("test-integration");
        var client = new InMemoryClient("IntegrationClient");

        var serverReceivedMessage = false;
        var clientReceivedMessage = false;

        serverService.MessageReceived += (s, e) => serverReceivedMessage = true;
        clientService.MessageReceived += (s, e) => clientReceivedMessage = true;

        // Act
        await serverService.StartServerAsync(serverTransport);
        await clientService.ConnectAsClientAsync(client, new Dictionary<string, object>
        {
            ["ServerName"] = "test-integration"
        });

        await Task.Delay(100); // Allow connection to establish

        // Client sends to server
        await clientService.SendMessageAsync("Hello from client!");
        await Task.Delay(100);

        // Server broadcasts to all clients
        await serverService.BroadcastMessageAsync("Hello from server!");
        await Task.Delay(100);

        // Assert
        Assert.IsTrue(serverReceivedMessage);
        Assert.IsTrue(clientReceivedMessage);
        Assert.AreEqual(MessagingMode.Server, serverService.Mode);
        Assert.AreEqual(MessagingMode.Client, clientService.Mode);

        // Cleanup
        await clientService.StopAsync();
        await serverService.StopAsync();
    }

    [TestMethod]
    public async Task MessagingService_ErrorHandling_Success()
    {
        // Arrange
        var service = new MessagingService();
        var errorOccurred = false;

        service.ErrorOccurred += (s, e) => errorOccurred = true;

        // Act - Try to connect to non-existent server
        var client = new InMemoryClient("TestClient");
        await service.ConnectAsClientAsync(client, new Dictionary<string, object>
        {
            ["ServerName"] = "non-existent-server"
        });

        await Task.Delay(100); // Allow error processing

        // Assert
        Assert.IsTrue(errorOccurred);
        Assert.AreEqual(MessagingMode.None, service.Mode);
        Assert.IsFalse(service.IsRunning);
    }
}

[TestClass]
public class TransportStressTests
{
    [TestMethod]
    public async Task InMemoryTransport_MultipleClients_Success()
    {
        // Arrange
        var transport = new InMemoryTransport("stress-test");
        var clients = new List<InMemoryClient>();
        var receivedMessages = new ConcurrentBag<Message>();

        const int clientCount = 10;
        const int messagesPerClient = 5;

        // Act
        await transport.StartAsync();

        // Create and connect multiple clients
        for (int i = 0; i < clientCount; i++)
        {
            var client = new InMemoryClient($"Client{i}");
            client.MessageReceived += (s, e) => receivedMessages.Add(e.Message);

            await client.ConnectAsync(new Dictionary<string, object>
            {
                ["ServerName"] = "stress-test"
            });

            clients.Add(client);
        }

        await Task.Delay(100); // Allow connections to establish

        // Send messages from each client
        var tasks = new List<Task>();
        for (int i = 0; i < clientCount; i++)
        {
            var clientIndex = i;
            var task = Task.Run(async () =>
            {
                for (int j = 0; j < messagesPerClient; j++)
                {
                    await clients[clientIndex].SendMessageAsync(new Message
                    {
                        Content = $"Message {j} from Client{clientIndex}",
                        Sender = $"Client{clientIndex}",
                        Type = MessageType.Text
                    });
                }
            });
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
        await Task.Delay(200); // Allow message processing

        // Assert
        Assert.AreEqual(clientCount, transport.Connections.Count);

        // Cleanup
        foreach (var client in clients)
        {
            await client.DisconnectAsync();
        }
        await transport.StopAsync();
    }

    [TestMethod]
    public async Task TcpTransport_ConcurrentConnections_Success()
    {
        // Arrange
        var transport = new TcpTransport();
        var clients = new List<TcpMessageClient>();
        var connectedClients = 0;

        transport.ClientConnected += (s, e) => Interlocked.Increment(ref connectedClients);

        const int TestPort = 9010;
        const int clientCount = 5;

        // Act
        await transport.StartAsync(new Dictionary<string, object> { ["Port"] = TestPort });

        // Create and connect multiple clients concurrently
        var connectionTasks = new List<Task>();
        for (int i = 0; i < clientCount; i++)
        {
            var clientIndex = i;
            var task = Task.Run(async () =>
            {
                var client = new TcpMessageClient();
                await client.ConnectAsync(new Dictionary<string, object>
                {
                    ["Host"] = "localhost",
                    ["Port"] = TestPort,
                    ["ClientName"] = $"ConcurrentClient{clientIndex}"
                });
                clients.Add(client);
            });
            connectionTasks.Add(task);
        }

        await Task.WhenAll(connectionTasks);
        await Task.Delay(200); // Allow connections to establish

        // Assert
        Assert.AreEqual(clientCount, connectedClients);
        Assert.AreEqual(clientCount, transport.Connections.Count);

        // Cleanup
        foreach (var client in clients)
        {
            await client.DisconnectAsync();
        }
        await transport.StopAsync();
    }
}

[TestClass]
public class TransportDisposalTests
{
    [TestMethod]
    public async Task TcpTransport_ProperDisposal_Success()
    {
        // Arrange
        var transport = new TcpTransport();
        var client = new TcpMessageClient();

        // Act
        await transport.StartAsync(new Dictionary<string, object> { ["Port"] = 9020 });
        await client.ConnectAsync(new Dictionary<string, object>
        {
            ["Host"] = "localhost",
            ["Port"] = 9020,
            ["ClientName"] = "DisposalTest"
        });

        await Task.Delay(100);

        // Dispose without explicit stop
        transport.Dispose();
        client.Dispose();

        // Assert - Should not throw exceptions
        Assert.IsFalse(transport.IsRunning);
        Assert.IsFalse(client.IsConnected);
    }

    [TestMethod]
    public async Task InMemoryTransport_ProperDisposal_Success()
    {
        // Arrange
        var transport = new InMemoryTransport("disposal-test");
        var client = new InMemoryClient("DisposalClient");

        // Act
        await transport.StartAsync();
        await client.ConnectAsync(new Dictionary<string, object>
        {
            ["ServerName"] = "disposal-test"
        });

        await Task.Delay(100);

        // Dispose without explicit stop
        transport.Dispose();
        client.Dispose();

        // Assert - Should not throw exceptions
        Assert.IsFalse(transport.IsRunning);
        Assert.IsFalse(client.IsConnected);
    }
}