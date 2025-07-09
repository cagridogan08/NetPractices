using System.Collections.Concurrent;
using System.Diagnostics;
using TESA.Desktop.Launcher.Services.TESA.Desktop.Launcher.Helpers;

namespace TESA.Desktop.Launcher.Services
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.IO;

    namespace TESA.Desktop.Launcher.Helpers
    {
        /// <summary>
        /// Configuration validation result
        /// </summary>
        public class ConfigurationValidationResult
        {
            public bool IsValid { get; set; } = true;
            public List<string> Errors { get; set; } = new();
            public List<string> Warnings { get; set; } = new();

            public string GetSummary()
            {
                var summary = IsValid ? "Configuration is valid" : "Configuration has errors";

                if (Errors.Count > 0)
                {
                    summary += $"\nErrors ({Errors.Count}):\n" + string.Join("\n", Errors.Select(e => $"  - {e}"));
                }

                if (Warnings.Count > 0)
                {
                    summary += $"\nWarnings ({Warnings.Count}):\n" + string.Join("\n", Warnings.Select(w => $"  - {w}"));
                }

                return summary;
            }
        }
    }
    /// <summary>
    /// ProcessLauncherService.cs - Enhanced Process Launching Service
    /// </summary>
    public class ProcessLauncherService : IDisposable
    {
        private readonly ILogger<ProcessLauncherService> _logger;
        private readonly ConcurrentDictionary<int, LaunchedProcessInfo> _runningProcesses;
        private readonly HashSet<string> _allowedExtensions;
        private readonly HashSet<string> _blockedPaths;
        private readonly int _maxConcurrentProcesses;
        private readonly TimeSpan _processStartTimeout;
        private bool _disposed = false;

        // Events for process lifecycle
        public event EventHandler<ProcessStartedEventArgs>? ProcessStarted;
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

        public ProcessLauncherService(ILogger<ProcessLauncherService> logger)
        {
            _logger = logger;
            _runningProcesses = new ConcurrentDictionary<int, LaunchedProcessInfo>();

            // Load configuration
            var config = ConfigurationHelper.GetLauncherConfiguration();
            _maxConcurrentProcesses = ConfigurationHelper.GetResourceLimit("MaxConcurrentProcesses", 50);
            _processStartTimeout = ConfigurationHelper.GetTimeout("Timeouts:ProcessStartTimeout", TimeSpan.FromSeconds(30));

            // Security settings
            _allowedExtensions = LoadAllowedExtensions();
            _blockedPaths = LoadBlockedPaths();

            _logger.LogInformation("ProcessLauncherService initialized");
            _logger.LogInformation($"Max concurrent processes: {_maxConcurrentProcesses}");
            _logger.LogInformation($"Allowed extensions: {string.Join(", ", _allowedExtensions)}");
        }

        public async Task<LaunchResponse> LaunchAsync(LaunchRequest request)
        {
            try
            {
                _logger.LogInformation($"Processing launch request for: {request.ProjectName} (ID: {request.RequestId})");

                // Validate concurrent process limit
                if (_runningProcesses.Count >= _maxConcurrentProcesses)
                {
                    var errorMessage = $"Maximum concurrent processes limit reached ({_maxConcurrentProcesses})";
                    _logger.LogWarning(errorMessage);
                    return CreateErrorResponse(errorMessage, request.RequestId);
                }

                // Validate request
                var validationResult = await ValidateRequestAsync(request);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning($"Invalid launch request: {validationResult.ErrorMessage}");
                    return CreateErrorResponse(validationResult.ErrorMessage, request.RequestId);
                }

                // Prepare process start info
                var startInfo = CreateProcessStartInfo(request);

                _logger.LogInformation($"Launching process: {startInfo.FileName} {startInfo.Arguments}");

                // Launch the process with timeout
                var process = await LaunchProcessWithTimeoutAsync(startInfo);

                if (process == null)
                {
                    var errorMessage = "Failed to start process within timeout period";
                    _logger.LogError(errorMessage);
                    return CreateErrorResponse(errorMessage, request.RequestId);
                }

                // Track the process
                var processInfo = new LaunchedProcessInfo
                {
                    ProcessId = process.Id,
                    ProjectName = request.ProjectName,
                    ProjectPath = request.ProjectPath,
                    RunnerPath = request.RunnerPath,
                    StartTime = DateTime.UtcNow,
                    RequestId = request.RequestId
                };

                _runningProcesses[process.Id] = processInfo;

                // Monitor process exit
                MonitorProcessExit(process);

                // Raise event
                ProcessStarted?.Invoke(this, new ProcessStartedEventArgs
                {
                    ProcessId = process.Id,
                    ProjectName = request.ProjectName,
                    StartTime = processInfo.StartTime
                });

                var successMessage = $"Successfully launched {request.ProjectName ?? "process"} with PID: {process.Id}";
                _logger.LogInformation(successMessage);

                return new LaunchResponse
                {
                    Success = true,
                    Message = successMessage,
                    ProcessId = process.Id,
                    Timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to launch process: {ex.Message}";
                _logger.LogError(ex, errorMessage);

                return CreateErrorResponse(errorMessage, request.RequestId);
            }
        }

        private async Task<ValidationResult> ValidateRequestAsync(LaunchRequest request)
        {
            if (request == null)
                return new ValidationResult(false, "Request cannot be null");

            if (string.IsNullOrWhiteSpace(request.RunnerPath))
                return new ValidationResult(false, "Runner path cannot be empty");

            // Security: Check file extension
            var extension = Path.GetExtension(request.RunnerPath);
            if (!_allowedExtensions.Contains(extension.ToLowerInvariant()))
            {
                return new ValidationResult(false, $"File extension '{extension}' is not allowed");
            }

            // Security: Check blocked paths
            var fullPath = Path.GetFullPath(request.RunnerPath);
            foreach (var blockedPath in _blockedPaths)
            {
                if (fullPath.StartsWith(blockedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return new ValidationResult(false, $"Path is blocked for security reasons: {blockedPath}");
                }
            }

            // Check if executable exists
            if (!File.Exists(request.RunnerPath))
                return new ValidationResult(false, $"Runner executable not found: {request.RunnerPath}");

            // Validate project path if provided
            if (!string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                if (!Directory.Exists(request.ProjectPath) && !File.Exists(request.ProjectPath))
                {
                    return new ValidationResult(false, $"Project path not found: {request.ProjectPath}");
                }
            }

            // Security: Check if file is digitally signed (if enabled)
            if (ConfigurationHelper.GetValue<bool>("LauncherConfiguration:Security:RequireDigitalSignature", false))
            {
                var isSignedValid = await ValidateDigitalSignatureAsync(request.RunnerPath);
                if (!isSignedValid)
                {
                    return new ValidationResult(false, "Executable is not digitally signed or signature is invalid");
                }
            }

            // Additional security: Check file size (prevent extremely large files)
            var fileInfo = new FileInfo(request.RunnerPath);
            if (fileInfo.Length > 500 * 1024 * 1024) // 500MB limit
            {
                return new ValidationResult(false, "Executable file is too large (>500MB)");
            }

            return new ValidationResult(true, null);
        }

        private async Task<bool> ValidateDigitalSignatureAsync(string filePath)
        {
            try
            {
                // This is a simplified check - in production you might want more robust validation
                await Task.Run(() =>
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                    // Add your digital signature validation logic here
                });

                return true; // Placeholder - implement actual signature validation
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to validate digital signature for: {filePath}");
                return false;
            }
        }

        private ProcessStartInfo CreateProcessStartInfo(LaunchRequest request)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = request.RunnerPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = Path.GetDirectoryName(request.RunnerPath),
                CreateNoWindow = false,
                ErrorDialog = false,
                LoadUserProfile = true
            };

            // Add arguments if project path is provided
            if (!string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                startInfo.Arguments = $"\"{request.ProjectPath}\"";
            }

            _logger.LogDebug($"Process start info - FileName: {startInfo.FileName}, Arguments: {startInfo.Arguments}, WorkingDirectory: {startInfo.WorkingDirectory}");

            return startInfo;
        }

        private async Task<Process?> LaunchProcessWithTimeoutAsync(ProcessStartInfo startInfo)
        {
            try
            {
                var process = new Process { StartInfo = startInfo };

                var startTask = Task.Run(() =>
                {
                    return process.Start() ? process : null;
                });

                var completedTask = await Task.WhenAny(startTask, Task.Delay(_processStartTimeout));

                if (completedTask == startTask)
                {
                    return await startTask;
                }
                else
                {
                    _logger.LogError($"Process start timed out after {_processStartTimeout.TotalSeconds} seconds");
                    try
                    {
                        process?.Kill();
                        process?.Dispose();
                    }
                    catch { }
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting process");
                return null;
            }
        }

        private void MonitorProcessExit(Process process)
        {
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += OnProcessExited;

                _logger.LogDebug($"Started monitoring process {process.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to setup process monitoring for PID {process.Id}");
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            if (sender is not Process process)
                return;

            try
            {
                var processId = process.Id;
                var exitCode = process.ExitCode;
                var exitTime = DateTime.UtcNow;

                _logger.LogInformation($"Process {processId} exited with code: {exitCode}");

                // Remove from tracking
                if (_runningProcesses.TryRemove(processId, out var processInfo))
                {
                    var duration = exitTime - processInfo.StartTime;
                    _logger.LogInformation($"Process {processId} ({processInfo.ProjectName}) ran for {duration:hh\\:mm\\:ss}");

                    // Raise event
                    ProcessExited?.Invoke(this, new ProcessExitedEventArgs
                    {
                        ProcessId = processId,
                        ProjectName = processInfo.ProjectName,
                        ExitCode = exitCode,
                        ExitTime = exitTime,
                        Duration = duration
                    });
                }
                else
                {
                    _logger.LogWarning($"Process {processId} not found in tracking dictionary");
                }

                // Dispose the process object
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling process exit");
            }
        }

        public List<LaunchedProcessInfo> GetRunningProcesses()
        {
            try
            {
                // Clean up dead processes first
                CleanupDeadProcesses();

                return _runningProcesses.Values.OrderBy(p => p.StartTime).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting running processes");
                return new List<LaunchedProcessInfo>();
            }
        }

        private void CleanupDeadProcesses()
        {
            var deadProcesses = new List<int>();

            foreach (var kvp in _runningProcesses)
            {
                try
                {
                    var process = Process.GetProcessById(kvp.Key);
                    if (process.HasExited)
                    {
                        deadProcesses.Add(kvp.Key);
                    }
                }
                catch (ArgumentException)
                {
                    // Process no longer exists
                    deadProcesses.Add(kvp.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Error checking process {kvp.Key}");
                }
            }

            foreach (var processId in deadProcesses)
            {
                if (_runningProcesses.TryRemove(processId, out var processInfo))
                {
                    _logger.LogDebug($"Cleaned up dead process {processId} ({processInfo.ProjectName})");
                }
            }
        }

        public async Task<bool> StopProcessAsync(int processId)
        {
            try
            {
                if (!_runningProcesses.TryGetValue(processId, out var processInfo))
                {
                    _logger.LogWarning($"Process {processId} not found in running processes");
                    return false;
                }

                _logger.LogInformation($"Stopping process {processId} ({processInfo.ProjectName})");

                var process = Process.GetProcessById(processId);

                // Try graceful shutdown first
                var gracefulShutdown = false;
                try
                {
                    gracefulShutdown = process.CloseMainWindow();
                    if (gracefulShutdown)
                    {
                        _logger.LogDebug($"Sent close message to process {processId}");

                        // Wait for graceful shutdown
                        var shutdownTimeout = ConfigurationHelper.GetTimeout("Timeouts:ProcessStopTimeout", TimeSpan.FromSeconds(10));
                        gracefulShutdown = process.WaitForExit((int)shutdownTimeout.TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to close main window for process {processId}");
                }

                // Force kill if graceful shutdown didn't work
                if (!gracefulShutdown && !process.HasExited)
                {
                    _logger.LogWarning($"Graceful shutdown failed for process {processId}, forcing termination");
                    process.Kill();

                    // Wait for forced termination
                    await Task.Run(() => process.WaitForExit(5000));
                }

                _logger.LogInformation($"Process {processId} stopped successfully");
                return true;
            }
            catch (ArgumentException)
            {
                _logger.LogWarning($"Process {processId} not found (already exited)");
                _runningProcesses.TryRemove(processId, out _);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to stop process {processId}");
                return false;
            }
        }

        public async Task<bool> StopAllProcessesAsync()
        {
            try
            {
                _logger.LogInformation("Stopping all running processes...");

                var processes = GetRunningProcesses();
                var stopTasks = processes.Select(p => StopProcessAsync(p.ProcessId));

                var results = await Task.WhenAll(stopTasks);
                var successCount = results.Count(r => r);

                _logger.LogInformation($"Stopped {successCount}/{processes.Count} processes");
                return successCount == processes.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping all processes");
                return false;
            }
        }

        public ProcessLauncherStats GetStats()
        {
            var currentProcesses = GetRunningProcesses();

            return new ProcessLauncherStats
            {
                TotalProcessesLaunched = GetTotalProcessesLaunched(),
                CurrentlyRunningProcesses = currentProcesses.Count,
                RunningProcesses = currentProcesses
            };
        }

        private int GetTotalProcessesLaunched()
        {
            // In a production system, you might want to persist this count
            // For now, we'll estimate based on current processes and some heuristics
            return _runningProcesses.Count + GetEstimatedCompletedProcesses();
        }

        private int GetEstimatedCompletedProcesses()
        {
            // This is a simple estimation - in production you might track this properly
            return 0;
        }

        private HashSet<string> LoadAllowedExtensions()
        {
            try
            {
                var extensions = ConfigurationHelper.GetValue<string[]>("LauncherConfiguration:Security:AllowedExecutableExtensions");
                return extensions?.ToHashSet(StringComparer.OrdinalIgnoreCase) ??
                       new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".cmd" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load allowed extensions, using defaults");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".cmd" };
            }
        }

        private HashSet<string> LoadBlockedPaths()
        {
            try
            {
                var paths = ConfigurationHelper.GetValue<string[]>("LauncherConfiguration:Security:BlockedPaths");
                var expandedPaths = paths?.Select(ConfigurationHelper.ExpandEnvironmentVariables).ToHashSet(StringComparer.OrdinalIgnoreCase) ??
                                   new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Add default blocked paths for security
                expandedPaths.Add(@"C:\Windows\System32");
                expandedPaths.Add(@"C:\Windows\SysWOW64");

                return expandedPaths;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load blocked paths, using defaults");
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    @"C:\Windows\System32",
                    @"C:\Windows\SysWOW64"
                };
            }
        }

        private LaunchResponse CreateErrorResponse(string errorMessage, string requestId = "")
        {
            return new LaunchResponse
            {
                Success = false,
                Error = errorMessage,
                Timestamp = DateTime.UtcNow
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _logger.LogInformation("Disposing ProcessLauncherService");

                    // Stop all running processes gracefully
                    StopAllProcessesAsync().Wait(TimeSpan.FromSeconds(30));

                    _runningProcesses.Clear();
                    _disposed = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing ProcessLauncherService");
                }
            }
        }

        private class ValidationResult
        {
            public bool IsValid { get; }
            public string? ErrorMessage { get; }

            public ValidationResult(bool isValid, string? errorMessage)
            {
                IsValid = isValid;
                ErrorMessage = errorMessage;
            }
        }
    }
}