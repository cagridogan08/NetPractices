using Messaging.ModelLibrary;
using Messaging.ModelLibrary.Udp;

namespace Messaging.ModelLibraryTests;

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