namespace TESA.Runner.ProjectService.Models
{
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
}
