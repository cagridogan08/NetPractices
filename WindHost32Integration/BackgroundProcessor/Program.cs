Console.WriteLine("Background Processor Starting...");

var server = new ProcessingServer();

// Handle Ctrl+C gracefully
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    server.Stop();
};

try
{
    await server.StartAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Server failed: {ex.Message}");
}

Console.WriteLine("Background Processor Stopped");