using Messaging.ModelLibrary.Tcp;

namespace Messaging.ModelLibraryTests;

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
}