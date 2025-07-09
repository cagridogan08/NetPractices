using Messaging.ModelLibrary;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Windows;
using TESA.Desktop.Launcher.Managers;
using TESA.Desktop.Launcher.Services;
using TESA.Desktop.Launcher.Services.TESA.Desktop.Launcher.Helpers;
using ErrorEventArgs = Messaging.ModelLibrary.ErrorEventArgs;
using Message = Messaging.ModelLibrary.Message;
using MessageBox = System.Windows.MessageBox;

namespace TESA.Desktop.Launcher
{
    /// <summary>
    /// App.xaml.cs - Main Application Entry Point
    /// </summary>
    public partial class App
    {
        private IServiceProvider _serviceProvider;
        private ILogger<App> _logger;
        private SystemTrayManager _systemTrayManager;
        private IMessagingService _messagingService;
        private IConfiguration _configuration;

        /// <summary>
        /// Gets the service provider for dependency injection
        /// </summary>
        public IServiceProvider Services => _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Load configuration first
                LoadConfiguration();

                // Configure services
                ConfigureServices();

                // Get logger
                _logger = _serviceProvider.GetRequiredService<ILogger<App>>();

                // Log startup
                _logger.LogInformation("=== TESA Desktop Launcher Starting ===");
                _logger.LogInformation($"Version: {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
                _logger.LogInformation($"Process ID: {Environment.ProcessId}");
                _logger.LogInformation($"User: {Environment.UserDomainName}\\{Environment.UserName}");

                // Initialize services
                InitializeServices();

                _logger.LogInformation("TESA Desktop Launcher started successfully");
            }
            catch (Exception ex)
            {
                var message = $"Failed to start TESA Desktop Launcher: {ex.Message}";
                _logger?.LogError(ex, message);
                MessageBox.Show(message, "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void LoadConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();

            _configuration = builder.Build();
        }

        public void ConfigureServices()
        {
            var services = new ServiceCollection();

            // Add configuration
            services.AddSingleton(_configuration);

            // Create logs directory
            var logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TESA", "Logs");
            Directory.CreateDirectory(logsDir);
            var logFilePath = Path.Combine(logsDir, $"launcher_{DateTime.Now:yyyyMMdd}.log");

            // Configure logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddFile(logFilePath);
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Register services
            services.AddSingleton<ProcessLauncherService>();
            services.AddSingleton<IMessagingService, MessagingService>();
            services.AddSingleton<SystemTrayManager>();
            services.AddSingleton<HealthCheckService>();
            services.AddSingleton<StatusService>();
            services.AddSingleton<ConfigurationHelper>();

            _serviceProvider = services.BuildServiceProvider();
        }

        private async void InitializeServices()
        {
            // Get services
            var processLauncher = _serviceProvider.GetRequiredService<ProcessLauncherService>();
            var healthCheck = _serviceProvider.GetRequiredService<HealthCheckService>();
            var statusService = _serviceProvider.GetRequiredService<StatusService>();

            // Initialize named pipe server
            _messagingService = _serviceProvider.GetRequiredService<IMessagingService>();

            // Get pipe configuration from settings
            var pipeName = _configuration["LauncherConfiguration:PipeName"] ?? "TESARunnerLauncher";
            var maxClients = int.Parse(_configuration["LauncherConfiguration:MaxConcurrentClients"] ?? "10");

            var server = new NamedPipeTransport(pipeName);
            var configuration = new Dictionary<string, object>
            {
                ["PipeName"] = pipeName,
                ["MaxConcurrentClients"] = maxClients
            };

            // Setup message handling
            _messagingService.MessageReceived += OnMessageReceived;
            _messagingService.Connected += OnClientConnected;
            _messagingService.Disconnected += OnClientDisconnected;
            _messagingService.ErrorOccurred += OnMessagingError;

            // Start the named pipe server
            var success = await _messagingService.StartServerAsync(server, configuration);
            if (!success)
            {
                _logger.LogError("Failed to start named pipe server");
                throw new InvalidOperationException("Failed to start named pipe server");
            }

            _logger.LogInformation($"Named pipe server started successfully on pipe: {pipeName}");

            // Initialize system tray
            _systemTrayManager = _serviceProvider.GetRequiredService<SystemTrayManager>();
            _systemTrayManager.Initialize();

            // Start health check
            healthCheck.StartMonitoring();
        }

        private async void OnMessageReceived(object sender, MessageEventArgs e)
        {
            try
            {
                _logger.LogInformation($"Received message from {e.Message.Sender}: {e.Message.Type}");

                if (e.Message.Type == MessageType.LaunchRequest)
                {
                    await HandleLaunchRequest(e.Message, e.Connection);
                }
                else if (e.Message.Type == MessageType.StatusRequest)
                {
                    await HandleStatusRequest(e.Message, e.Connection);
                }
                else if (e.Message.Type == MessageType.HealthCheck)
                {
                    await HandleHealthCheckRequest(e.Message, e.Connection);
                }
                else
                {
                    _logger.LogWarning($"Unknown message type: {e.Message.Type}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
            }
        }

        private async Task HandleLaunchRequest(Message message, ConnectionInfo connection)
        {
            try
            {
                var launchRequest = System.Text.Json.JsonSerializer.Deserialize<LaunchRequest>(message.Content);
                var processLauncher = _serviceProvider.GetRequiredService<ProcessLauncherService>();
                var statusService = _serviceProvider.GetRequiredService<StatusService>();

                var response = await processLauncher.LaunchAsync(launchRequest);
                statusService.OnRequestProcessed();

                var responseMessage = new Message
                {
                    Type = MessageType.LaunchResponse,
                    Content = System.Text.Json.JsonSerializer.Serialize(response),
                    Sender = "TESALauncher",
                    Receiver = message.Sender
                };

                await _messagingService.SendMessageAsync(responseMessage.Content, connection.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling launch request");

                var errorResponse = new LaunchResponse
                {
                    Success = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                };

                var responseMessage = new Message
                {
                    Type = MessageType.LaunchResponse,
                    Content = System.Text.Json.JsonSerializer.Serialize(errorResponse),
                    Sender = "TESALauncher",
                    Receiver = message.Sender
                };

                await _messagingService.SendMessageAsync(responseMessage.Content, connection.Id);
            }
        }

        private async Task HandleStatusRequest(Message message, ConnectionInfo connection)
        {
            try
            {
                var statusService = _serviceProvider.GetRequiredService<StatusService>();
                var status = statusService.GetCurrentStatus();

                var responseMessage = new Message
                {
                    Type = MessageType.StatusResponse,
                    Content = System.Text.Json.JsonSerializer.Serialize(status),
                    Sender = "TESALauncher",
                    Receiver = message.Sender
                };

                await _messagingService.SendMessageAsync(responseMessage.Content, connection.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling status request");
            }
        }

        private async Task HandleHealthCheckRequest(Message message, ConnectionInfo connection)
        {
            try
            {
                var healthService = _serviceProvider.GetRequiredService<HealthCheckService>();
                var health = healthService.GetHealthCheckSummary();

                var responseMessage = new Message
                {
                    Type = MessageType.HealthCheckResponse,
                    Content = System.Text.Json.JsonSerializer.Serialize(health),
                    Sender = "TESALauncher",
                    Receiver = message.Sender
                };

                await _messagingService.SendMessageAsync(responseMessage.Content, connection.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling health check request");
            }
        }

        private void OnClientConnected(object sender, ConnectionEventArgs e)
        {
            _logger.LogInformation($"Client connected: {e.Connection.Name} ({e.Connection.Id})");
        }

        private void OnClientDisconnected(object sender, ConnectionEventArgs e)
        {
            _logger.LogInformation($"Client disconnected: {e.Connection.Name} ({e.Connection.Id})");
        }

        private void OnMessagingError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.Exception, $"Messaging error: {e.Error}");
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.LogInformation("TESA Desktop Launcher shutting down");

                // Cleanup services
                _systemTrayManager?.Dispose();

                if (_messagingService != null)
                {
                    await _messagingService.StopAsync();
                }

                _logger?.LogInformation("TESA Desktop Launcher shutdown complete");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during shutdown");
            }

            base.OnExit(e);
        }
    }
}