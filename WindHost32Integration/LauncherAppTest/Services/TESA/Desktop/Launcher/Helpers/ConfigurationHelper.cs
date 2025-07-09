using System.IO;
using Microsoft.Extensions.Configuration;

namespace TESA.Desktop.Launcher.Services.TESA.Desktop.Launcher.Helpers;

/// <summary>
/// ConfigurationHelper.cs - Configuration Management Helper
/// </summary>
/// <summary>
/// Configuration helper for accessing app settings
/// </summary>
public class ConfigurationHelper
{
    private static IConfiguration _configuration;

    public static void Initialize(IConfiguration configuration = null)
    {
        if (configuration != null)
        {
            _configuration = configuration;
            return;
        }

        var builder = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        _configuration = builder.Build();
    }

    public static T GetValue<T>(string key, T defaultValue = default(T))
    {
        try
        {
            return _configuration.GetValue<T>(key) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public static string GetLogDirectory()
    {
        var path = GetValue<string>("Paths:LogDirectory", "%LOCALAPPDATA%\\TESA\\Logs");
        return ExpandEnvironmentVariables(path);
    }

    public static string GetConfigDirectory()
    {
        var path = GetValue<string>("Paths:ConfigDirectory", "%LOCALAPPDATA%\\TESA\\Config");
        return ExpandEnvironmentVariables(path);
    }

    public static string GetTempDirectory()
    {
        var path = GetValue<string>("Paths:TempDirectory", "%TEMP%\\TESA");
        return ExpandEnvironmentVariables(path);
    }

    public static string ExpandEnvironmentVariables(string path)
    {
        return Environment.ExpandEnvironmentVariables(path);
    }

    public static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public static TimeSpan GetTimeout(string key, TimeSpan defaultValue)
    {
        try
        {
            var value = GetValue<string>(key);
            if (TimeSpan.TryParse(value, out var timeSpan))
            {
                return timeSpan;
            }
        }
        catch { }

        return defaultValue;
    }

    public static int GetResourceLimit(string key, int defaultValue)
    {
        return GetValue<int>($"Resources:{key}", defaultValue);
    }

    public static LauncherConfiguration GetLauncherConfiguration()
    {
        return new LauncherConfiguration
        {
            HealthCheckInterval = GetTimeout("LauncherConfiguration:HealthCheckInterval", TimeSpan.FromSeconds(30)),
            MaxMemoryUsageMB = GetValue<double>("LauncherConfiguration:MaxMemoryUsageMB", 150.0),
            MaxResponseTime = GetTimeout("LauncherConfiguration:MaxResponseTime", TimeSpan.FromSeconds(5)),
            EnableSystemTray = GetValue<bool>("LauncherConfiguration:EnableSystemTray", true),
            EnableHealthChecks = GetValue<bool>("LauncherConfiguration:EnableHealthChecks", true),
            PipeName = GetValue<string>("LauncherConfiguration:PipeName", "TESARunnerLauncher"),
            MaxConcurrentClients = GetValue<int>("LauncherConfiguration:MaxConcurrentClients", 10)
        };
    }

    public static ConfigurationValidationResult ValidateConfiguration()
    {
        var result = new ConfigurationValidationResult();

        try
        {
            // Validate required paths
            var logDir = GetLogDirectory();
            var configDir = GetConfigDirectory();
            var tempDir = GetTempDirectory();

            if (!Directory.Exists(Path.GetDirectoryName(logDir)))
            {
                result.Warnings.Add($"Log directory parent does not exist: {Path.GetDirectoryName(logDir)}");
            }

            // Validate pipe name
            var pipeName = GetValue<string>("LauncherConfiguration:PipeName");
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                result.Errors.Add("PipeName cannot be empty");
                result.IsValid = false;
            }

            // Validate numeric values
            var maxClients = GetValue<int>("LauncherConfiguration:MaxConcurrentClients", 10);
            if (maxClients <= 0)
            {
                result.Errors.Add("MaxConcurrentClients must be greater than 0");
                result.IsValid = false;
            }

            var maxMemory = GetValue<double>("LauncherConfiguration:MaxMemoryUsageMB", 150.0);
            if (maxMemory <= 0)
            {
                result.Errors.Add("MaxMemoryUsageMB must be greater than 0");
                result.IsValid = false;
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Configuration validation error: {ex.Message}");
            result.IsValid = false;
        }

        return result;
    }
}