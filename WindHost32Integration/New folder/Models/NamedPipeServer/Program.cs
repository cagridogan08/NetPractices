using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Messaging.ModelLibrary;
using Messaging.ModelLibrary.Pipe;

namespace NamedPipeServer
{
    class Program
    {
        private static readonly string PipeName = "MyNamedPipe";
        private static bool _isRunning = true;
        private static IMessagingService _messagingService;
        static async Task Main(string[] args)
        {
            _messagingService = new Messaging.ModelLibrary.MessagingService();
            var server = new NamedPipeTransport(PipeName);
            var configuration = new Dictionary<string, object>
            {
                ["PipeName"] = PipeName,
                ["ServerName"] = ".",
                ["Timeout"] = 5000,
                ["ClientName"] = "TestServer"
            };
            var c = _messagingService.StartServerAsync(server, configuration);
            Console.WriteLine("Named Pipe Server Starting...");
            Console.WriteLine($"Pipe Name: {PipeName}");
            Console.WriteLine("Press 'q' to quit the server\n");
            // Start the server in a separate task
            var serverTask = Task.Run(StartServer);

            // Wait for user input to quit
            while (_isRunning)
            {
                var key = Console.ReadKey(true);
                if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                {
                    _isRunning = false;
                    Console.WriteLine("Shutting down server...");
                }
            }

            await serverTask;
        }

        private static async Task StartServer()
        {
            int clientCounter = 0;

            while (_isRunning)
            {
                try
                {
                    using (var pipeServer = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous))
                    {
                        Console.WriteLine("Waiting for client connection...");

                        // Wait for a client to connect
                        await pipeServer.WaitForConnectionAsync();
                        clientCounter++;

                        Console.WriteLine($"Client #{clientCounter} connected!");

                        // Handle the client in a separate task
                        var clientTask = Task.Run(() => HandleClient(pipeServer, clientCounter));
                        await clientTask;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Server error: {ex.Message}");
                }
            }
        }

        private static async Task HandleClient(NamedPipeServerStream pipeServer, int clientId)
        {
            try
            {
                using (var reader = new StreamReader(pipeServer, Encoding.UTF8, false, 1024, true))
                using (var writer = new StreamWriter(pipeServer, Encoding.UTF8, 1024, true))
                {
                    writer.AutoFlush = true;

                    // Send welcome message
                    await writer.WriteLineAsync($"Welcome to the server! You are client #{clientId}");

                    string message;
                    while ((message = await reader.ReadLineAsync()) != null)
                    {
                        Console.WriteLine($"[Client #{clientId}]: {message}");

                        if (message.ToLower() == "quit" || message.ToLower() == "exit")
                        {
                            await writer.WriteLineAsync("Goodbye!");
                            break;
                        }
                        else if (message.ToLower() == "time")
                        {
                            await writer.WriteLineAsync($"Server time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        }
                        else if (message.ToLower().StartsWith("echo "))
                        {
                            string echoMessage = message.Substring(5);
                            await writer.WriteLineAsync($"Echo: {echoMessage}");
                        }
                        else
                        {
                            await writer.WriteLineAsync($"Server received: {message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client #{clientId}: {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"Client #{clientId} disconnected.");
            }
        }
    }
}