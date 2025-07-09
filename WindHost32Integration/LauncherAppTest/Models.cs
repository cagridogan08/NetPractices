
namespace TESA.Desktop.Launcher
{
    /// <summary>
    /// Models.cs - Data Models and DTOs
    /// </summary>

    #region Launch Request/Response Models

    public class LaunchRequest
    {
        public string RunnerPath { get; set; } = "";
        public string? ProjectPath { get; set; }
        public string? ProjectName { get; set; }
        public string RequestId { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class LaunchResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Error { get; set; }
        public int? ProcessId { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion

    #region Process Models

    public class LaunchedProcessInfo
    {
        public int ProcessId { get; set; }
        public string? ProjectName { get; set; }
        public string? ProjectPath { get; set; }
        public string RunnerPath { get; set; } = "";
        public DateTime StartTime { get; set; }
        public string RequestId { get; set; } = "";
    }

    public class ProcessLauncherStats
    {
        public int TotalProcessesLaunched { get; set; }
        public int CurrentlyRunningProcesses { get; set; }
        public List<LaunchedProcessInfo> RunningProcesses { get; set; } = new();
    }

    #endregion

    #region Status Models

    public class LauncherStatus
    {
        public bool IsRunning { get; set; }
        public int ProcessId { get; set; }
        public DateTime StartTime { get; set; }
        public int TotalRequestsProcessed { get; set; }
        public DateTime LastRequestTime { get; set; }
        public bool IsHealthy { get; set; }
        public string LogFilePath { get; set; } = "";
        public string Version { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public string UserName { get; set; } = "";
        public string MachineName { get; set; } = "";
        public double MemoryUsage { get; set; }
        public TimeSpan Uptime { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class RequestStatistics
    {
        public int TotalRequestsProcessed { get; set; }
        public DateTime LastRequestTime { get; set; }
        public double RequestsPerHour { get; set; }
        public TimeSpan AverageTimeBetweenRequests { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    #endregion

    #region System Information Models

    public class SystemInfo
    {
        public string OperatingSystem { get; set; } = "";
        public int ProcessorCount { get; set; }
        public string MachineName { get; set; } = "";
        public string UserDomainName { get; set; } = "";
        public string UserName { get; set; } = "";
        public bool Is64BitOperatingSystem { get; set; }
        public bool Is64BitProcess { get; set; }
        public string CLRVersion { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
        public long TotalPhysicalMemory { get; set; }
    }

    public class PerformanceCounters
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public TimeSpan TotalProcessorTime { get; set; }
        public long WorkingSet64 { get; set; }
        public long VirtualMemorySize64 { get; set; }
        public long PrivateMemorySize64 { get; set; }
        public long PagedMemorySize64 { get; set; }
        public long NonpagedSystemMemorySize64 { get; set; }
        public long PagedSystemMemorySize64 { get; set; }
        public int HandleCount { get; set; }
        public int ThreadCount { get; set; }
    }

    #endregion

    #region Health Check Models

    public class HealthCheckResult
    {
        public DateTime CheckTime { get; set; }
        public bool IsHealthy { get; set; }
        public string ErrorMessage { get; set; } = "";
        public List<HealthIssue> Issues { get; set; } = new();
    }

    public class HealthIssue
    {
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public HealthIssueSeverity Severity { get; set; }
    }

    public enum HealthIssueSeverity
    {
        Info,
        Warning,
        Critical
    }

    public class HealthCheckSummary
    {
        public bool IsHealthy { get; set; }
        public DateTime LastCheckTime { get; set; }
        public string LastError { get; set; } = "";
        public TimeSpan CheckInterval { get; set; }
        public int TotalIssues { get; set; }
    }

    #endregion

    #region Named Pipe Models

    public class NamedPipeServerInfo
    {
        public string PipeName { get; set; } = "";
        public bool IsListening { get; set; }
        public DateTime StartTime { get; set; }
        public int ActiveClientCount { get; set; }
    }

    #endregion

    #region Configuration Models

    public class LauncherConfiguration
    {
        public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);
        public double MaxMemoryUsageMB { get; set; } = 150.0;
        public TimeSpan MaxResponseTime { get; set; } = TimeSpan.FromSeconds(5);
        public string LogLevel { get; set; } = "Information";
        public bool EnableSystemTray { get; set; } = true;
        public bool EnableHealthChecks { get; set; } = true;
        public string PipeName { get; set; } = "TESARunnerLauncher";
        public int MaxConcurrentClients { get; set; } = 10;
    }

    #endregion

    #region UI Models

    public class StatusWindowViewModel
    {
        public LauncherStatus Status { get; set; } = new();
        public List<LaunchedProcessInfo> RunningProcesses { get; set; } = new();
        public HealthCheckSummary HealthSummary { get; set; } = new();
        public SystemInfo SystemInfo { get; set; } = new();
        public PerformanceCounters PerformanceCounters { get; set; } = new();
        public RequestStatistics RequestStatistics { get; set; } = new();
        public NamedPipeServerInfo PipeServerInfo { get; set; } = new();
    }

    #endregion

    #region Event Models

    public class ProcessStartedEventArgs : EventArgs
    {
        public int ProcessId { get; set; }
        public string? ProjectName { get; set; }
        public DateTime StartTime { get; set; }
    }

    public class ProcessExitedEventArgs : EventArgs
    {
        public int ProcessId { get; set; }
        public string? ProjectName { get; set; }
        public int ExitCode { get; set; }
        public DateTime ExitTime { get; set; }
        public TimeSpan Duration { get; set; }
    }

    public class HealthCheckEventArgs : EventArgs
    {
        public bool IsHealthy { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CheckTime { get; set; }
        public List<HealthIssue> Issues { get; set; } = new();
    }

    #endregion

    #region Error Models

    public class LauncherError
    {
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
        public string? Details { get; set; }
        public DateTime Timestamp { get; set; }
        public ErrorSeverity Severity { get; set; }
    }

    public enum ErrorSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    #endregion

    #region Extension Methods

    public static class ModelExtensions
    {
        public static string GetFormattedUptime(this LauncherStatus status)
        {
            return status.Uptime.ToString(@"dd\.hh\:mm\:ss");
        }

        public static string GetFormattedMemoryUsage(this LauncherStatus status)
        {
            return $"{status.MemoryUsage:F2} MB";
        }

        public static string GetLastRequestText(this LauncherStatus status)
        {
            if (status.LastRequestTime == DateTime.MinValue)
                return "Never";

            var timeSince = DateTime.UtcNow - status.LastRequestTime;
            if (timeSince.TotalMinutes < 1)
                return "Just now";
            if (timeSince.TotalHours < 1)
                return $"{(int)timeSince.TotalMinutes}m ago";
            if (timeSince.TotalDays < 1)
                return $"{(int)timeSince.TotalHours}h ago";

            return $"{(int)timeSince.TotalDays}d ago";
        }

        public static string GetSeverityDisplayText(this HealthIssueSeverity severity)
        {
            return severity switch
            {
                HealthIssueSeverity.Info => "ℹ️ Info",
                HealthIssueSeverity.Warning => "⚠️ Warning",
                HealthIssueSeverity.Critical => "🔴 Critical",
                _ => "❓ Unknown"
            };
        }

        public static string GetFormattedFileSize(this long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int suffixIndex = 0;

            while (value >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                value /= 1024;
                suffixIndex++;
            }

            return $"{value:F2} {suffixes[suffixIndex]}";
        }
    }

    #endregion
}