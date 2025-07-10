using Messaging.ModelLibrary.Tcp;

namespace Messaging.ModelLibraryTests;

[TestClass]
public class TransportStressTests
{


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