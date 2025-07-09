using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Serilog;
using TESA.Runner.ProjectService.Services;
using MessageBox = System.Windows.MessageBox;

namespace TESA.Runner.ProjectService
{
    /// <summary>
    /// Program.cs - Application Entry Point
    /// </summary>
    public static class Program
    {
        private static Mutex? _mutex;
        private static ILogger<App>? _logger;
        private static string serviceName = "TESA.Runner.ProjectService";
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                if (args is { Length: 1 })
                {
                    try
                    {
                        string executablePath = Path.Combine(AppContext.BaseDirectory, "TESA.Runner.ProjectService.exe");

                        if (args[0] is "/Install")
                        {
                            // Set the path to the executable
                            string exePath = Path.Combine(AppContext.BaseDirectory, "TESA.Runner.ProjectService.exe");

                            // Add to startup
                            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                            key?.SetValue(serviceName, $"\"{exePath}\"");

                            Log.Information("Application registered to startup: {Path}", exePath);
                        }
                        else if (args[0] is "/Uninstall")
                        {
                            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                            key?.DeleteValue(serviceName, false);

                            Log.Information("Application removed from startup.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                // Setup basic logging for startup
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                _logger = loggerFactory.CreateLogger<App>();

                _logger.LogInformation("=== TESA Desktop Launcher Starting ===");
                _logger.LogInformation($"Version: {GetApplicationVersion()}");
                _logger.LogInformation($"Process ID: {Environment.ProcessId}");
                _logger.LogInformation($"Command line: {Environment.CommandLine}");
                _logger.LogInformation($"Working directory: {Environment.CurrentDirectory}");

                // Parse command line arguments
                var options = ParseCommandLineArguments(args);

                // Check for single instance if not disabled
                if (!options.AllowMultipleInstances && !EnsureSingleInstance())
                {
                    _logger.LogWarning("Another instance of TESA Desktop Launcher is already running");

                    if (!options.Silent)
                    {
                        MessageBox.Show(
                            "TESA Desktop Launcher is already running.\n\nCheck the system tray for the running instance.",
                            "Already Running",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }

                    return;
                }

                // Handle special command line options
                if (options.ShowHelp)
                {
                    ShowHelp();
                    return;
                }

                if (options.ShowVersion)
                {
                    ShowVersion();
                    return;
                }

                // Start the WPF application
                _logger.LogInformation("Starting WPF application...");

                var app = new App();

                // Set startup options
                if (options.StartMinimized)
                {
                    // The app will handle this in its startup
                    app.Properties["StartMinimized"] = true;
                }

                app.Run();

                _logger.LogInformation("Application shutdown complete");
            }
            catch (Exception ex)
            {
                var errorMessage = $"Fatal error during application startup: {ex.Message}";
                _logger?.LogCritical(ex, errorMessage);

                // Show error to user if possible
                try
                {
                    MessageBox.Show(
                        $"{errorMessage}\n\nSee logs for more details.",
                        "Fatal Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                catch
                {
                    // If we can't show a message box, at least write to console
                    Console.WriteLine($"FATAL ERROR: {errorMessage}");
                    Console.WriteLine($"Exception: {ex}");
                }

                Environment.Exit(1);
            }
            finally
            {
                // Clean up mutex
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
        }

        private static bool EnsureSingleInstance()
        {
            const string mutexName = "Global\\TESADesktopLauncher_SingleInstance";

            try
            {
                _mutex = new Mutex(true, mutexName, out bool createdNew);

                if (!createdNew)
                {
                    // Another instance is running, try to bring it to front
                    BringExistingInstanceToFront();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error checking for existing instance");
                return true; // Allow startup if we can't check
            }
        }

        private static void BringExistingInstanceToFront()
        {
            try
            {
                // Find existing process
                var currentProcess = Process.GetCurrentProcess();
                var processes = Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var process in processes)
                {
                    if (process.Id != currentProcess.Id)
                    {
                        // Found another instance, try to bring it to front
                        // This is limited by Windows security, but we can try
                        NativeMethods.SetForegroundWindow(process.MainWindowHandle);

                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SW_RESTORE);
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to bring existing instance to front");
            }
        }



        private static CommandLineOptions ParseCommandLineArguments(string[] args)
        {
            var options = new CommandLineOptions();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();

                switch (arg)
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options.ShowHelp = true;
                        break;

                    case "--version":
                    case "-v":
                        options.ShowVersion = true;
                        break;

                    case "--silent":
                    case "-s":
                        options.Silent = true;
                        break;

                    case "--allow-multiple":
                    case "-m":
                        options.AllowMultipleInstances = true;
                        break;

                    case "--minimized":
                    case "--start-minimized":
                        options.StartMinimized = true;
                        break;

                    case "--debug":
                        options.DebugMode = true;
                        break;

                    default:
                        if (arg.StartsWith("--"))
                        {
                            _logger?.LogWarning($"Unknown command line option: {arg}");
                        }
                        break;
                }
            }

            return options;
        }

        private static void ShowHelp()
        {
            var helpText = $@"
TESA Desktop Launcher v{GetApplicationVersion()}

Usage: TESA.Desktop.Launcher.exe [options]

Options:
  --help, -h, /?           Show this help message
  --version, -v            Show version information
  --silent, -s             Run silently (no dialogs)
  --allow-multiple, -m     Allow multiple instances
  --minimized              Start minimized to system tray
  --debug                  Enable debug mode

Examples:
  TESA.Desktop.Launcher.exe
  TESA.Desktop.Launcher.exe --minimized
  TESA.Desktop.Launcher.exe --silent --allow-multiple

The application will run in the system tray and listen for launch requests
from the TESA Windows Service via named pipes.

For more information, visit: https://github.com/your-org/tesa
";

            Console.WriteLine(helpText);

            if (Environment.UserInteractive)
            {
                MessageBox.Show(helpText, "TESA Desktop Launcher - Help", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static void ShowVersion()
        {
            var version = GetApplicationVersion();
            var versionInfo = $"TESA Desktop Launcher v{version}";

            Console.WriteLine(versionInfo);

            if (Environment.UserInteractive)
            {
                MessageBox.Show(versionInfo, "Version Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string GetApplicationVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    /// <summary>
    /// Command line options
    /// </summary>
    public class CommandLineOptions
    {
        public bool ShowHelp { get; set; }
        public bool ShowVersion { get; set; }
        public bool Silent { get; set; }
        public bool AllowMultipleInstances { get; set; }
        public bool StartMinimized { get; set; }
        public bool DebugMode { get; set; }
    }

    /// <summary>
    /// Native Windows API methods
    /// </summary>
    internal static class NativeMethods
    {
        public const int SW_RESTORE = 9;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
