using Messaging.ModelLibrary;
using Messaging.ModelLibrary.WebSocket;

namespace Messaging.ModelLibraryTests;

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