using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TESA.Desktop.Launcher.Services
{
    /// <summary>
    /// StatusService.cs - Application Status Management
    /// </summary>
    public class StatusService
    {
        private readonly ILogger<StatusService> _logger;
        private readonly DateTime _startTime;
        private readonly string _logFilePath;
        private DateTime _lastRequestTime = DateTime.MinValue;
        private int _totalRequestsProcessed = 0;
        private readonly object _statsLock = new object();

        public StatusService(ILogger<StatusService> logger)
        {
            _logger = logger;
            _startTime = DateTime.UtcNow;

            // Set up log file path
            var logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TESA", "Logs");
            _logFilePath = Path.Combine(logsDir, $"launcher_{DateTime.Now:yyyyMMdd}.log");
        }

        /// <summary>
        /// Gets the current application status
        /// </summary>
        public LauncherStatus GetCurrentStatus()
        {
            try
            {
                lock (_statsLock)
                {
                    var currentProcess = Process.GetCurrentProcess();

                    return new LauncherStatus
                    {
                        IsRunning = true,
                        ProcessId = currentProcess.Id,
                        StartTime = _startTime,
                        TotalRequestsProcessed = _totalRequestsProcessed,
                        LastRequestTime = _lastRequestTime,
                        IsHealthy = true, // This could be enhanced with more health checks
                        LogFilePath = _logFilePath,
                        Version = GetApplicationVersion(),
                        WorkingDirectory = Environment.CurrentDirectory,
                        UserName = $"{Environment.UserDomainName}\\{Environment.UserName}",
                        MachineName = Environment.MachineName,
                        MemoryUsage = GetMemoryUsage(),
                        Uptime = DateTime.UtcNow - _startTime
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting current status");
                return CreateErrorStatus(ex);
            }
        }

        /// <summary>
        /// Records that a request has been processed
        /// </summary>
        public void OnRequestProcessed()
        {
            lock (_statsLock)
            {
                _totalRequestsProcessed++;
                _lastRequestTime = DateTime.UtcNow;
            }

            _logger.LogDebug($"Request processed. Total: {_totalRequestsProcessed}");
        }

        /// <summary>
        /// Gets the log file path
        /// </summary>
        public string GetLogFilePath()
        {
            return _logFilePath;
        }

        /// <summary>
        /// Gets application version
        /// </summary>
        public string GetApplicationVersion()
        {
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get application version");
                return "Unknown";
            }
        }

        /// <summary>
        /// Gets current memory usage in MB
        /// </summary>
        public double GetMemoryUsage()
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                return Math.Round(currentProcess.WorkingSet64 / 1024.0 / 1024.0, 2);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get memory usage");
                return 0;
            }
        }

        /// <summary>
        /// Gets system information
        /// </summary>
        public SystemInfo GetSystemInfo()
        {
            try
            {
                return new SystemInfo
                {
                    OperatingSystem = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    MachineName = Environment.MachineName,
                    UserDomainName = Environment.UserDomainName,
                    UserName = Environment.UserName,
                    Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                    Is64BitProcess = Environment.Is64BitProcess,
                    CLRVersion = Environment.Version.ToString(),
                    WorkingDirectory = Environment.CurrentDirectory,
                    TotalPhysicalMemory = GetTotalPhysicalMemory()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system info");
                return new SystemInfo
                {
                    OperatingSystem = "Unknown",
                    ProcessorCount = 0,
                    MachineName = "Unknown",
                    UserDomainName = "Unknown",
                    UserName = "Unknown",
                    Is64BitOperatingSystem = false,
                    Is64BitProcess = false,
                    CLRVersion = "Unknown",
                    WorkingDirectory = "Unknown",
                    TotalPhysicalMemory = 0
                };
            }
        }

        /// <summary>
        /// Gets performance counters
        /// </summary>
        public PerformanceCounters GetPerformanceCounters()
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();

                return new PerformanceCounters
                {
                    ProcessId = currentProcess.Id,
                    ProcessName = currentProcess.ProcessName,
                    StartTime = currentProcess.StartTime,
                    TotalProcessorTime = currentProcess.TotalProcessorTime,
                    WorkingSet64 = currentProcess.WorkingSet64,
                    VirtualMemorySize64 = currentProcess.VirtualMemorySize64,
                    PrivateMemorySize64 = currentProcess.PrivateMemorySize64,
                    PagedMemorySize64 = currentProcess.PagedMemorySize64,
                    NonpagedSystemMemorySize64 = currentProcess.NonpagedSystemMemorySize64,
                    PagedSystemMemorySize64 = currentProcess.PagedSystemMemorySize64,
                    HandleCount = currentProcess.HandleCount,
                    ThreadCount = currentProcess.Threads.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting performance counters");
                return new PerformanceCounters();
            }
        }

        /// <summary>
        /// Resets statistics
        /// </summary>
        public void ResetStatistics()
        {
            lock (_statsLock)
            {
                _totalRequestsProcessed = 0;
                _lastRequestTime = DateTime.MinValue;
            }

            _logger.LogInformation("Statistics reset");
        }

        /// <summary>
        /// Gets request processing statistics
        /// </summary>
        public RequestStatistics GetRequestStatistics()
        {
            lock (_statsLock)
            {
                var uptime = DateTime.UtcNow - _startTime;
                var requestsPerHour = uptime.TotalHours > 0 ? _totalRequestsProcessed / uptime.TotalHours : 0;

                return new RequestStatistics
                {
                    TotalRequestsProcessed = _totalRequestsProcessed,
                    LastRequestTime = _lastRequestTime,
                    RequestsPerHour = Math.Round(requestsPerHour, 2),
                    AverageTimeBetweenRequests = GetAverageTimeBetweenRequests(),
                    Uptime = uptime
                };
            }
        }

        private LauncherStatus CreateErrorStatus(Exception ex)
        {
            return new LauncherStatus
            {
                IsRunning = false,
                ProcessId = 0,
                StartTime = _startTime,
                TotalRequestsProcessed = _totalRequestsProcessed,
                LastRequestTime = _lastRequestTime,
                IsHealthy = false,
                LogFilePath = _logFilePath,
                Version = "Error",
                WorkingDirectory = "Error",
                UserName = "Error",
                MachineName = "Error",
                MemoryUsage = 0,
                Uptime = TimeSpan.Zero,
                ErrorMessage = ex.Message
            };
        }

        private long GetTotalPhysicalMemory()
        {
            try
            {
                // This is a simplified way to get memory info
                // In a real application, you might want to use performance counters
                // or WMI for more accurate memory information
                var gc = GC.GetTotalMemory(false);
                return gc;
            }
            catch
            {
                return 0;
            }
        }

        private TimeSpan GetAverageTimeBetweenRequests()
        {
            if (_totalRequestsProcessed <= 1)
                return TimeSpan.Zero;

            var totalTime = DateTime.UtcNow - _startTime;
            return TimeSpan.FromTicks(totalTime.Ticks / _totalRequestsProcessed);
        }

        /// <summary>
        /// Checks if the application is healthy
        /// </summary>
        public bool IsHealthy()
        {
            try
            {
                // Basic health checks
                var status = GetCurrentStatus();

                // Check if we're running
                if (!status.IsRunning)
                    return false;

                // Check if log file is accessible
                if (!string.IsNullOrEmpty(_logFilePath))
                {
                    var logDir = Path.GetDirectoryName(_logFilePath);
                    if (!Directory.Exists(logDir))
                        return false;
                }

                // Check memory usage (warning if over 100MB)
                if (status.MemoryUsage > 100)
                {
                    _logger.LogWarning($"High memory usage: {status.MemoryUsage:F2} MB");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return false;
            }
        }
    }
}