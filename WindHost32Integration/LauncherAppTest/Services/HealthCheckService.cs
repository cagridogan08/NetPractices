using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Timer = System.Threading.Timer;

namespace TESA.Desktop.Launcher.Services
{
    /// <summary>
    /// HealthCheckService.cs - Health Monitoring Service
    /// </summary>
    public class HealthCheckService : IDisposable
    {
        private readonly ILogger<HealthCheckService> _logger;
        private readonly StatusService _statusService;
        private readonly Timer _healthCheckTimer;
        private readonly object _healthLock = new object();

        private bool _isHealthy = true;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private string _lastHealthCheckError = string.Empty;
        private HealthCheckResult _lastResult;
        private bool _disposed = false;

        // Health check configuration
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromSeconds(30);
        private readonly double _maxMemoryUsageMB = 150.0;
        private readonly TimeSpan _maxResponseTime = TimeSpan.FromSeconds(5);

        public HealthCheckService(ILogger<HealthCheckService> logger, StatusService statusService)
        {
            _logger = logger;
            _statusService = statusService;

            // Initialize health check timer
            _healthCheckTimer = new Timer(
                callback: PerformHealthCheck,
                state: null,
                dueTime: TimeSpan.FromSeconds(10), // First check after 10 seconds
                period: _healthCheckInterval);
        }

        public bool IsHealthy()
        {
            lock (_healthLock)
            {
                // Consider unhealthy if last check was too long ago
                if (DateTime.UtcNow - _lastHealthCheck > _healthCheckInterval.Add(TimeSpan.FromSeconds(30)))
                {
                    return false;
                }

                return _isHealthy;
            }
        }

        public void StartMonitoring()
        {
            try
            {
                _logger.LogInformation("Starting health check monitoring");

                // Perform initial health check
                PerformHealthCheck(null);

                _logger.LogInformation($"Health check monitoring started (interval: {_healthCheckInterval.TotalSeconds} seconds)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start health check monitoring");
                throw;
            }
        }

        public void StopMonitoring()
        {
            try
            {
                _logger.LogInformation("Stopping health check monitoring");
                _healthCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                _logger.LogInformation("Health check monitoring stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping health check monitoring");
            }
        }

        private void PerformHealthCheck(object state)
        {
            try
            {
                var result = RunHealthChecks();

                lock (_healthLock)
                {
                    _isHealthy = result.IsHealthy;
                    _lastHealthCheck = DateTime.UtcNow;
                    _lastHealthCheckError = result.ErrorMessage;
                    _lastResult = result;
                }

                if (result.IsHealthy)
                {
                    _logger.LogDebug("Health check passed");
                }
                else
                {
                    _logger.LogWarning($"Health check failed: {result.ErrorMessage}");

                    // Log detailed issues
                    foreach (var issue in result.Issues)
                    {
                        _logger.LogWarning($"Health issue: {issue.Category} - {issue.Description}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing health check");

                lock (_healthLock)
                {
                    _isHealthy = false;
                    _lastHealthCheck = DateTime.UtcNow;
                    _lastHealthCheckError = ex.Message;
                }
            }
        }

        private HealthCheckResult RunHealthChecks()
        {
            var result = new HealthCheckResult
            {
                CheckTime = DateTime.UtcNow,
                IsHealthy = true
            };

            try
            {
                // Check 1: Memory usage
                CheckMemoryUsage(result);

                // Check 2: Log file accessibility
                CheckLogFileAccess(result);

                // Check 3: Process status
                CheckProcessStatus(result);

                // Check 4: System resources
                CheckSystemResources(result);

                // Check 5: Application responsiveness
                CheckApplicationResponsiveness(result);

                // Set overall health status
                result.IsHealthy = result.Issues.Count == 0;

                if (!result.IsHealthy)
                {
                    result.ErrorMessage = $"Health check failed with {result.Issues.Count} issue(s)";
                }
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.ErrorMessage = $"Health check exception: {ex.Message}";
                result.Issues.Add(new HealthIssue
                {
                    Category = "Exception",
                    Description = ex.Message,
                    Severity = HealthIssueSeverity.Critical
                });
            }

            return result;
        }

        private void CheckMemoryUsage(HealthCheckResult result)
        {
            try
            {
                var memoryUsage = _statusService.GetMemoryUsage();

                if (memoryUsage > _maxMemoryUsageMB)
                {
                    result.Issues.Add(new HealthIssue
                    {
                        Category = "Memory",
                        Description = $"High memory usage: {memoryUsage:F2} MB (max: {_maxMemoryUsageMB} MB)",
                        Severity = HealthIssueSeverity.Warning
                    });
                }

                // Critical memory usage check
                if (memoryUsage > _maxMemoryUsageMB * 2)
                {
                    result.Issues.Add(new HealthIssue
                    {
                        Category = "Memory",
                        Description = $"Critical memory usage: {memoryUsage:F2} MB",
                        Severity = HealthIssueSeverity.Critical
                    });
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new HealthIssue
                {
                    Category = "Memory",
                    Description = $"Failed to check memory usage: {ex.Message}",
                    Severity = HealthIssueSeverity.Warning
                });
            }
        }

        private void CheckLogFileAccess(HealthCheckResult result)
        {
            try
            {
                var logPath = _statusService.GetLogFilePath();

                if (!string.IsNullOrEmpty(logPath))
                {
                    var logDir = Path.GetDirectoryName(logPath);

                    if (!Directory.Exists(logDir))
                    {
                        result.Issues.Add(new HealthIssue
                        {
                            Category = "Logging",
                            Description = $"Log directory does not exist: {logDir}",
                            Severity = HealthIssueSeverity.Warning
                        });
                    }
                    else if (!File.Exists(logPath))
                    {
                        result.Issues.Add(new HealthIssue
                        {
                            Category = "Logging",
                            Description = $"Log file does not exist: {logPath}",
                            Severity = HealthIssueSeverity.Warning
                        });
                    }
                    else
                    {
                        // Check if log file is writable
                        var testFile = Path.Combine(logDir, "health_check_test.tmp");
                        try
                        {
                            File.WriteAllText(testFile, "test");
                            File.Delete(testFile);
                        }
                        catch (Exception ex)
                        {
                            result.Issues.Add(new HealthIssue
                            {
                                Category = "Logging",
                                Description = $"Cannot write to log directory: {ex.Message}",
                                Severity = HealthIssueSeverity.Warning
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new HealthIssue
                {
                    Category = "Logging",
                    Description = $"Failed to check log file access: {ex.Message}",
                    Severity = HealthIssueSeverity.Warning
                });
            }
        }

        private void CheckProcessStatus(HealthCheckResult result)
        {
            try
            {
                using var currentProcess = Process.GetCurrentProcess();

                if (currentProcess.HasExited)
                {
                    result.Issues.Add(new HealthIssue
                    {
                        Category = "Process",
                        Description = "Current process has exited",
                        Severity = HealthIssueSeverity.Critical
                    });
                }

                // Check if process is responding
                if (!currentProcess.Responding)
                {
                    result.Issues.Add(new HealthIssue
                    {
                        Category = "Process",
                        Description = "Process is not responding",
                        Severity = HealthIssueSeverity.Critical
                    });
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new HealthIssue
                {
                    Category = "Process",
                    Description = $"Failed to check process status: {ex.Message}",
                    Severity = HealthIssueSeverity.Warning
                });
            }
        }

        private void CheckSystemResources(HealthCheckResult result)
        {
            try
            {
                // Check available disk space
                var logPath = _statusService.GetLogFilePath();
                if (!string.IsNullOrEmpty(logPath))
                {
                    var logDir = Path.GetDirectoryName(logPath);
                    if (Directory.Exists(logDir))
                    {
                        var drive = new DriveInfo(Path.GetPathRoot(logDir));
                        var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

                        if (freeSpaceGB < 1.0) // Less than 1GB free
                        {
                            result.Issues.Add(new HealthIssue
                            {
                                Category = "DiskSpace",
                                Description = $"Low disk space: {freeSpaceGB:F2} GB available",
                                Severity = freeSpaceGB < 0.5 ? HealthIssueSeverity.Critical : HealthIssueSeverity.Warning
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new HealthIssue
                {
                    Category = "DiskSpace",
                    Description = $"Failed to check disk space: {ex.Message}",
                    Severity = HealthIssueSeverity.Warning
                });
            }
        }

        private void CheckApplicationResponsiveness(HealthCheckResult result)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Simple responsiveness test - get current status
                var status = _statusService.GetCurrentStatus();

                stopwatch.Stop();

                if (stopwatch.Elapsed > _maxResponseTime)
                {
                    result.Issues.Add(new HealthIssue
                    {
                        Category = "Responsiveness",
                        Description = $"Slow response time: {stopwatch.ElapsedMilliseconds}ms (max: {_maxResponseTime.TotalMilliseconds}ms)",
                        Severity = HealthIssueSeverity.Warning
                    });
                }
            }
            catch (Exception ex)
            {
                result.Issues.Add(new HealthIssue
                {
                    Category = "Responsiveness",
                    Description = $"Failed to check responsiveness: {ex.Message}",
                    Severity = HealthIssueSeverity.Warning
                });
            }
        }

        public HealthCheckResult GetLastHealthCheckResult()
        {
            lock (_healthLock)
            {
                return _lastResult ?? new HealthCheckResult
                {
                    CheckTime = DateTime.MinValue,
                    IsHealthy = false,
                    ErrorMessage = "No health check performed yet"
                };
            }
        }

        public HealthCheckSummary GetHealthCheckSummary()
        {
            lock (_healthLock)
            {
                return new HealthCheckSummary
                {
                    IsHealthy = _isHealthy,
                    LastCheckTime = _lastHealthCheck,
                    LastError = _lastHealthCheckError,
                    CheckInterval = _healthCheckInterval,
                    TotalIssues = _lastResult?.Issues?.Count ?? 0
                };
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _logger.LogInformation("Disposing HealthCheckService");
                    StopMonitoring();
                    _healthCheckTimer?.Dispose();
                    _disposed = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing HealthCheckService");
                }
            }
        }
    }
}