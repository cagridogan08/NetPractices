using System.Collections.Concurrent;
using Messaging.ModelLibrary;
using Messaging.ModelLibrary.Tcp;

namespace Messaging.ModelLibraryTests;

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