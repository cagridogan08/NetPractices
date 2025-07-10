using System.IO.Pipes;
using System.Text;

namespace NamedPipeClient
{
    class Program
    {
        private static readonly string PipeName = "MyNamedPipe";
        private static readonly string ServerName = "."; // Local machine

        static async Task Main(string[] args)
        {
            Console.WriteLine("Named Pipe Client Starting...");
            Console.WriteLine($"Connecting to pipe: {PipeName}");
            Console.WriteLine("Available commands:");
            Console.WriteLine("  time - Get server time");
            Console.WriteLine("  echo <message> - Echo a message");
            Console.WriteLine("  quit/exit - Disconnect from server");
            Console.WriteLine();
            var _messagingService = new Messaging.ModelLibrary.MessagingService();
            try
            {
                var client = new Messaging.ModelLibrary.Pipe.NamedPipeClient();
                var configuration = new Dictionary<string, object>
                {
                    ["ServerName"] = ".",
                    ["PipeName"] = PipeName,
                    ["Timeout"] = 5000,
                    ["ClientName"] = "Test"
                };

                var success = await _messagingService.ConnectAsClientAsync(client, configuration);
                _messagingService.MessageReceived += (sender, e) =>
                {
                    Console.WriteLine($"[Server]: {e.Message}");
                };
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Connection timed out. Make sure the server is running.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Client error: {ex.Message}");
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}