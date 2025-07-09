using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace TESA.Desktop.Launcher.Utilities
{
    /// <summary>
    /// StartupManager.cs - Windows Startup Management
    /// </summary>
    public class StartupManager
    {
        private const string ApplicationName = "TESA Desktop Launcher";
        private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public bool IsStartupInstalled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                if (key == null)
                    return false;

                var value = key.GetValue(ApplicationName) as string;
                return !string.IsNullOrEmpty(value);
            }
            catch (Exception ex)
            {
                throw new StartupManagementException($"Failed to check startup status: {ex.Message}", ex);
            }
        }

        public void InstallStartupEntry()
        {
            try
            {
                var executablePath = GetExecutablePath();

                if (!File.Exists(executablePath))
                {
                    throw new StartupManagementException($"Executable not found: {executablePath}");
                }

                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null)
                {
                    throw new StartupManagementException("Failed to open registry key for writing");
                }

                // Add the application to startup with silent parameter
                var commandLine = $"\"{executablePath}\" --silent";
                key.SetValue(ApplicationName, commandLine, RegistryValueKind.String);

                // Verify the installation
                if (!IsStartupInstalled())
                {
                    throw new StartupManagementException("Failed to verify startup installation");
                }
            }
            catch (SecurityException ex)
            {
                throw new StartupManagementException("Insufficient permissions to modify startup registry", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new StartupManagementException("Access denied when modifying startup registry", ex);
            }
            catch (Exception ex)
            {
                throw new StartupManagementException($"Failed to install startup entry: {ex.Message}", ex);
            }
        }

        public void UninstallStartupEntry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
                if (key == null)
                {
                    throw new StartupManagementException("Failed to open registry key for writing");
                }

                // Remove the application from startup
                key.DeleteValue(ApplicationName, false);

                // Verify the removal
                if (IsStartupInstalled())
                {
                    throw new StartupManagementException("Failed to verify startup removal");
                }
            }
            catch (SecurityException ex)
            {
                throw new StartupManagementException("Insufficient permissions to modify startup registry", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new StartupManagementException("Access denied when modifying startup registry", ex);
            }
            catch (Exception ex)
            {
                throw new StartupManagementException($"Failed to uninstall startup entry: {ex.Message}", ex);
            }
        }

        public string GetCurrentStartupCommand()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
                if (key == null)
                    return string.Empty;

                return key.GetValue(ApplicationName) as string ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new StartupManagementException($"Failed to get current startup command: {ex.Message}", ex);
            }
        }

        private string GetExecutablePath()
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                var mainModule = currentProcess.MainModule;

                if (mainModule?.FileName != null)
                {
                    return mainModule.FileName;
                }

                // Fallback to assembly location
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                return assembly.Location;
            }
            catch (Exception ex)
            {
                throw new StartupManagementException($"Failed to determine executable path: {ex.Message}", ex);
            }
        }

        public StartupInfo GetStartupInfo()
        {
            return new StartupInfo
            {
                IsInstalled = IsStartupInstalled(),
                ExecutablePath = GetExecutablePath(),
                CurrentCommand = GetCurrentStartupCommand(),
                ApplicationName = ApplicationName,
                RegistryKeyPath = RegistryKeyPath
            };
        }
    }

    /// <summary>
    /// Information about the startup configuration
    /// </summary>
    public class StartupInfo
    {
        public bool IsInstalled { get; set; }
        public string ExecutablePath { get; set; } = "";
        public string CurrentCommand { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string RegistryKeyPath { get; set; } = "";
    }

    /// <summary>
    /// Exception thrown when startup management operations fail
    /// </summary>
    public class StartupManagementException : Exception
    {
        public StartupManagementException(string message) : base(message) { }
        public StartupManagementException(string message, Exception innerException) : base(message, innerException) { }
    }
}