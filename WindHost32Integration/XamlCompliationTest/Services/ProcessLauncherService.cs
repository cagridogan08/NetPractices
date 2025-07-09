using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using TESA.Runner.ProjectService.Models;
using Timer = System.Threading.Timer;

namespace TESA.Runner.ProjectService.Services
{
    public class ProcessStartedEventArgs : EventArgs
    {
        public int ProcessId { get; set; }
        public string? ProjectName { get; set; }
        public DateTime StartTime { get; set; }
        public string? ProjectPath { get; set; }
        public string RequestId { get; set; } = "";
    }

    public class ProcessExitedEventArgs : EventArgs
    {
        public int ProcessId { get; set; }
        public string? ProjectName { get; set; }
        public int ExitCode { get; set; }
        public DateTime ExitTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string RequestId { get; set; } = "";
    }

    public class ProcessLaunchRequest
    {
        public string ExecutablePath { get; set; } = "";
        public string? Arguments { get; set; }
        public string? WorkingDirectory { get; set; }
        public string? ProjectName { get; set; }
        public string? ProjectPath { get; set; }
        public string RequestId { get; set; } = Guid.NewGuid().ToString();
        public bool CreateWindow { get; set; } = true;
        public ProcessWindowStyle WindowStyle { get; set; } = ProcessWindowStyle.Normal;
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    }

    public class ProcessLaunchResult
    {
        public bool Success { get; set; }
        public int? ProcessId { get; set; }
        public string? Error { get; set; }
        public string RequestId { get; set; } = "";
        public DateTime LaunchedAt { get; set; }
    }

    internal class ProcessLauncherService : IDisposable
    {
        private readonly ILogger<ProcessLauncherService> _logger;
        private readonly ConcurrentDictionary<int, LaunchedProcessInfo> _runningProcesses;
        private readonly HashSet<string> _allowedExtensions;
        private readonly HashSet<string> _blockedPaths;
        private readonly int _maxConcurrentProcesses;
        private readonly TimeSpan _processStartTimeout;
        private readonly Timer _cleanupTimer;
        private bool _disposed = false;

        public event EventHandler<ProcessStartedEventArgs>? ProcessStarted;
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

        public int RunningProcessCount => _runningProcesses.Count;
        public IEnumerable<LaunchedProcessInfo> RunningProcesses => _runningProcesses.Values.ToList();

        public ProcessLauncherService(ILogger<ProcessLauncherService> logger, int maxConcurrentProcesses = 5, TimeSpan? processStartTimeout = null)
        {
            _logger = logger;
            _runningProcesses = new ConcurrentDictionary<int, LaunchedProcessInfo>();
            _allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".cmd" };
            _blockedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _maxConcurrentProcesses = maxConcurrentProcesses;
            _processStartTimeout = processStartTimeout ?? TimeSpan.FromSeconds(30);

            // Setup cleanup timer to periodically clean up dead processes
            _cleanupTimer = new Timer(CleanupDeadProcesses, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            _logger.LogInformation("ProcessLauncherService initialized");
            _logger.LogInformation($"Max concurrent processes: {maxConcurrentProcesses}");
            _logger.LogInformation($"Allowed extensions: {string.Join(", ", _allowedExtensions)}");
        }

        public async Task<ProcessLaunchResult> LaunchProcessAsync(ProcessLaunchRequest request)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ProcessLauncherService));

            try
            {
                // Validate request
                var validationResult = ValidateLaunchRequest(request);
                if (!validationResult.Success)
                {
                    return validationResult;
                }

                // Check concurrent process limit
                if (_runningProcesses.Count >= _maxConcurrentProcesses)
                {
                    return new ProcessLaunchResult
                    {
                        Success = false,
                        Error = $"Maximum concurrent processes limit reached ({_maxConcurrentProcesses})",
                        RequestId = request.RequestId
                    };
                }

                // Setup process start info
                var startInfo = new ProcessStartInfo
                {
                    FileName = request.ExecutablePath,
                    Arguments = request.Arguments ?? "",
                    WorkingDirectory = request.WorkingDirectory ?? Path.GetDirectoryName(request.ExecutablePath),
                    UseShellExecute = request.CreateWindow,
                    CreateNoWindow = !request.CreateWindow,
                    WindowStyle = request.WindowStyle,
                    LoadUserProfile = true
                };

                // Add environment variables
                foreach (var kvp in request.EnvironmentVariables)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }

                // Create and configure process
                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };

                var startTime = DateTime.UtcNow;
                var processInfo = new LaunchedProcessInfo
                {
                    ProjectName = request.ProjectName,
                    ProjectPath = request.ProjectPath,
                    RunnerPath = request.ExecutablePath,
                    StartTime = startTime,
                    RequestId = request.RequestId
                };

                // Setup exit handler before starting
                process.Exited += (sender, e) => HandleProcessExited(sender as Process, processInfo, startTime);

                // Start the process
                if (!process.Start())
                {
                    return new ProcessLaunchResult
                    {
                        Success = false,
                        Error = "Failed to start process",
                        RequestId = request.RequestId
                    };
                }

                // Update process info with actual process ID
                processInfo.ProcessId = process.Id;

                // Track the process
                _runningProcesses.TryAdd(process.Id, processInfo);

                _logger.LogInformation("Process started successfully - PID: {ProcessId}, Project: {ProjectName}, RequestId: {RequestId}",
                    process.Id, request.ProjectName, request.RequestId);

                // Fire event
                ProcessStarted?.Invoke(this, new ProcessStartedEventArgs
                {
                    ProcessId = process.Id,
                    ProjectName = request.ProjectName,
                    ProjectPath = request.ProjectPath,
                    StartTime = startTime,
                    RequestId = request.RequestId
                });

                return new ProcessLaunchResult
                {
                    Success = true,
                    ProcessId = process.Id,
                    RequestId = request.RequestId,
                    LaunchedAt = startTime
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error launching process for request {RequestId}: {ExecutablePath}",
                    request.RequestId, request.ExecutablePath);

                return new ProcessLaunchResult
                {
                    Success = false,
                    Error = ex.Message,
                    RequestId = request.RequestId
                };
            }
        }

        public async Task<bool> StopProcessAsync(int processId, TimeSpan? timeout = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ProcessLauncherService));

            try
            {
                if (!_runningProcesses.TryGetValue(processId, out var processInfo))
                {
                    _logger.LogWarning("Attempted to stop process {ProcessId} that is not tracked", processId);
                    return false;
                }

                var process = Process.GetProcessById(processId);
                var actualTimeout = timeout ?? TimeSpan.FromSeconds(10);

                _logger.LogInformation("Stopping process {ProcessId} ({ProjectName})", processId, processInfo.ProjectName);

                // Try graceful shutdown first
                if (!process.CloseMainWindow())
                {
                    _logger.LogInformation("CloseMainWindow failed for process {ProcessId}, using Kill", processId);
                    process.Kill();
                }

                // Wait for exit
                if (!process.WaitForExit((int)actualTimeout.TotalMilliseconds))
                {
                    _logger.LogWarning("Process {ProcessId} did not exit within timeout, forcing kill", processId);
                    process.Kill();
                    process.WaitForExit(5000); // Give it 5 more seconds
                }

                return true;
            }
            catch (ArgumentException)
            {
                // Process doesn't exist anymore
                _runningProcesses.TryRemove(processId, out _);
                _logger.LogInformation("Process {ProcessId} no longer exists", processId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping process {ProcessId}", processId);
                return false;
            }
        }

        public async Task<int> StopAllProcessesAsync(TimeSpan? timeout = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ProcessLauncherService));

            var processIds = _runningProcesses.Keys.ToList();
            var stoppedCount = 0;

            foreach (var processId in processIds)
            {
                if (await StopProcessAsync(processId, timeout))
                {
                    stoppedCount++;
                }
            }

            _logger.LogInformation("Stopped {StoppedCount} of {TotalCount} processes", stoppedCount, processIds.Count);
            return stoppedCount;
        }

        public LaunchedProcessInfo? GetProcessInfo(int processId)
        {
            _runningProcesses.TryGetValue(processId, out var processInfo);
            return processInfo;
        }

        public IEnumerable<LaunchedProcessInfo> GetProcessesByProject(string projectName)
        {
            return _runningProcesses.Values
                .Where(p => string.Equals(p.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public void AddBlockedPath(string path)
        {
            _blockedPaths.Add(Path.GetFullPath(path));
            _logger.LogInformation("Added blocked path: {Path}", path);
        }

        public void RemoveBlockedPath(string path)
        {
            _blockedPaths.Remove(Path.GetFullPath(path));
            _logger.LogInformation("Removed blocked path: {Path}", path);
        }

        private ProcessLaunchResult ValidateLaunchRequest(ProcessLaunchRequest request)
        {
            var result = new ProcessLaunchResult { RequestId = request.RequestId };

            // Check if executable exists
            if (!File.Exists(request.ExecutablePath))
            {
                result.Success = false;
                result.Error = $"Executable not found: {request.ExecutablePath}";
                return result;
            }

            // Check allowed extensions
            var extension = Path.GetExtension(request.ExecutablePath);
            if (!_allowedExtensions.Contains(extension))
            {
                result.Success = false;
                result.Error = $"File extension '{extension}' is not allowed";
                return result;
            }

            // Check blocked paths
            var fullPath = Path.GetFullPath(request.ExecutablePath);
            if (_blockedPaths.Any(blocked => fullPath.StartsWith(blocked, StringComparison.OrdinalIgnoreCase)))
            {
                result.Success = false;
                result.Error = $"Path is blocked: {request.ExecutablePath}";
                return result;
            }

            // Validate working directory if specified
            if (!string.IsNullOrEmpty(request.WorkingDirectory) && !Directory.Exists(request.WorkingDirectory))
            {
                result.Success = false;
                result.Error = $"Working directory not found: {request.WorkingDirectory}";
                return result;
            }

            result.Success = true;
            return result;
        }

        private void HandleProcessExited(Process? process, LaunchedProcessInfo processInfo, DateTime startTime)
        {
            if (process == null) return;

            var exitTime = DateTime.UtcNow;
            var duration = exitTime - startTime;
            var exitCode = process.ExitCode;

            // Remove from tracking
            _runningProcesses.TryRemove(process.Id, out _);

            _logger.LogInformation("Process exited - PID: {ProcessId}, Project: {ProjectName}, Duration: {Duration}, ExitCode: {ExitCode}",
                process.Id, processInfo.ProjectName, duration, exitCode);

            // Fire event
            ProcessExited?.Invoke(this, new ProcessExitedEventArgs
            {
                ProcessId = process.Id,
                ProjectName = processInfo.ProjectName,
                ExitCode = exitCode,
                ExitTime = exitTime,
                Duration = duration,
                RequestId = processInfo.RequestId
            });

            // Dispose the process
            process.Dispose();
        }

        private void CleanupDeadProcesses(object? state)
        {
            if (_disposed) return;

            try
            {
                var deadProcessIds = new List<int>();

                foreach (var kvp in _runningProcesses)
                {
                    try
                    {
                        var process = Process.GetProcessById(kvp.Key);
                        if (process.HasExited)
                        {
                            deadProcessIds.Add(kvp.Key);
                        }
                        process.Dispose();
                    }
                    catch (ArgumentException)
                    {
                        // Process doesn't exist
                        deadProcessIds.Add(kvp.Key);
                    }
                }

                foreach (var deadProcessId in deadProcessIds)
                {
                    if (_runningProcesses.TryRemove(deadProcessId, out var processInfo))
                    {
                        _logger.LogInformation("Cleaned up dead process: {ProcessId} ({ProjectName})",
                            deadProcessId, processInfo.ProjectName);
                    }
                }

                if (deadProcessIds.Count > 0)
                {
                    _logger.LogDebug("Cleanup completed: removed {Count} dead processes", deadProcessIds.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during process cleanup");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _cleanupTimer?.Dispose();

                // Stop all running processes
                var stopTask = StopAllProcessesAsync(TimeSpan.FromSeconds(5));
                stopTask.Wait(TimeSpan.FromSeconds(10));

                _logger.LogInformation("ProcessLauncherService disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing ProcessLauncherService");
            }
        }
    }
}