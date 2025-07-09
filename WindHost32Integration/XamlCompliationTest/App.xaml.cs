using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;
using Microsoft.AspNetCore.Builder;
using Serilog;
using TESA.ModelLibrary.Project;
using TESA.Runner.ProjectService.Controllers;
using TESA.Runner.ProjectService.Services;
using MessageBox = System.Windows.MessageBox;

namespace TESA.Runner.ProjectService
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App
    {
        private IHost _serviceProvider;
        private ILogger<App> _logger;
        private SystemTrayManager _systemTrayManager;
        public IHost ServiceProvider => _serviceProvider;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Load configuration first
                LoadConfiguration();

                // Configure services

                // Get logger
                _logger = _serviceProvider.Services.GetRequiredService<ILogger<App>>();

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

        private async void InitializeServices()
        {
            // Get services
            var statusService = _serviceProvider.Services.GetRequiredService<StatusService>();


            // Initialize system tray
            _systemTrayManager = _serviceProvider.Services.GetRequiredService<SystemTrayManager>();
            _systemTrayManager.Initialize();

            // Start health check
            //healthCheck.StartMonitoring();
        }



        private async void LoadConfiguration()
        {
            var builder = Host.CreateDefaultBuilder()
                .ConfigureHostConfiguration(builder =>
                {
                    builder.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    builder.AddEnvironmentVariables();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(p =>
                    {
                        p.Limits.MaxRequestBodySize = 10 * 1024 * 1024 * 30; // Set max request body size to 10 MB
                    });
                    webBuilder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.Build();
                        var url = $"http://0.0.0.0:{ProjectConstants.ProjectImportExportPort}";
                        if (!string.IsNullOrEmpty(url))
                        {
                            webBuilder.UseUrls(url); // Set the URL
                        }
                    });

                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddHttpContextAccessor();
                        services.AddSingleton<Services.ProjectService>();
                        services.AddSingleton<SystemTrayManager>();
                        services.AddSingleton<StatusService>();
                        services.AddControllers();
                        services.AddSignalR();

                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapControllers();
                            endpoints.MapHub<TesaRunnerMessageHub>("/tesaRunner");
                        });
                    });
                }).ConfigureLogging((_, loggingBuilder) =>
                {
                    loggingBuilder.ClearProviders(); // Removes all default providers, including EventLog
                    loggingBuilder.AddConsole();    // Use Console logging instead
                    loggingBuilder.AddSerilog();    // Use Serilog for logging
                    loggingBuilder.SetMinimumLevel(LogLevel.Information);
                }); ;
            _serviceProvider = builder.Build();
            await _serviceProvider.RunAsync();
        }
    }

}
