using Messaging.ModelLibrary;
using Messaging.ModelLibrary.InMemory;

namespace Messaging.ModelLibraryTests;

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