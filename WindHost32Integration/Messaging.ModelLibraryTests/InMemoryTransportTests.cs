using System.Collections.Concurrent;
using Messaging.ModelLibrary;
using Messaging.ModelLibrary.InMemory;

namespace Messaging.ModelLibraryTests;

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