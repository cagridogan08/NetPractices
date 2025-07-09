using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
namespace TESA.Runner.ProjectService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RunnerController : ControllerBase
{
    private readonly ILogger<RunnerController> _logger;
    private readonly IHubContext<TesaRunnerMessageHub> _hubContext;
    private readonly Services.ProjectService _projectService;
    private readonly string _exportedProjectsDirectory;
    private static readonly Dictionary<int, RunningProjectInfo> RunningProjects = new();

    private readonly string? _runnerPath;

    public RunnerController(
        ILogger<RunnerController> logger,
        IHubContext<TesaRunnerMessageHub> hubContext,
        Services.ProjectService projectService)
    {
        _logger = logger;
        _hubContext = hubContext;
        _projectService = projectService;
        _exportedProjectsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExportedProjects");

        if (System.IO.File.Exists(Constants.RunnerLocation))
        {
            _runnerPath = Constants.RunnerLocation;
        }
        else if (Environment.GetEnvironmentVariable(Constants.RunnerEnviromentVariableName) is { } environmentVariable
                && System.IO.File.Exists(environmentVariable))
        {
            _runnerPath = environmentVariable;
        }
        else
        {
            _runnerPath = Constants.RunnerLocation;
        }
    }

    [HttpGet]
    [Route("health")]
    public Task<IActionResult> GetHealth()
    {
        var runningProcesses = Process.GetProcessesByName(Constants.RunnerProcess);

        var health = new
        {
            RunnerPath = _runnerPath,
            RunnerExists = System.IO.File.Exists(_runnerPath),
            IsRunning = runningProcesses.Length > 0,
            RunningProcesses = runningProcesses.Select(p => new
            {
                ProcessId = p.Id,
                StartTime = p.StartTime,
                ProjectInfo = RunningProjects.TryGetValue(p.Id, out var project) ? project : null
            }).ToArray(),
            ExportedProjectsDirectory = _exportedProjectsDirectory,
            ExportedProjectsExists = Directory.Exists(_exportedProjectsDirectory),
            Timestamp = DateTime.UtcNow
        };

        return Task.FromResult<IActionResult>(System.IO.File.Exists(_runnerPath) ? Ok(health) : NotFound(health));
    }

    [HttpPost]
    [Route("start-project")]
    public async Task<IActionResult> StartProject([FromQuery] string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            var errorMsg = "Project name is required";
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "error", Message = errorMsg, Timestamp = DateTime.UtcNow });
            return BadRequest(new { Error = errorMsg });
        }

        try
        {
            // Validate project exists
            var project = await _projectService.GetProjectByNameAndVersion(projectName);
            if (project == null)
            {
                var errorMsg = $"Project '{projectName}' not found";
                await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "error", Message = errorMsg, Timestamp = DateTime.UtcNow });
                return NotFound(new { Error = errorMsg });
            }

            if (!System.IO.File.Exists(_runnerPath))
            {
                var errorMsg = $"Runner not found at {_runnerPath}";
                await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "error", Message = errorMsg, Timestamp = DateTime.UtcNow });
                return NotFound(new { Error = errorMsg });
            }

            // Check if project is already running
            var existingProject = RunningProjects.Values.FirstOrDefault(p => p.ProjectName == projectName);
            if (existingProject != null)
            {
                var warningMsg = $"Project '{projectName}' is already running (PID: {existingProject.ProcessId})";
                _logger.LogWarning(warningMsg);
                await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "warning", Message = warningMsg, Timestamp = DateTime.UtcNow });
                return BadRequest(new { Error = warningMsg, ExistingProcess = existingProject });
            }

            // Start the runner with project path
            var projectPath = project.FullPath;
            var result = await StartRunnerWithProject(_runnerPath, projectName, projectPath);

            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Failed to start project '{projectName}': {ex.Message}";
            _logger.LogError(ex, "Error starting project: {ProjectName}", projectName);
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "error", Message = errorMsg, Timestamp = DateTime.UtcNow });
            return Problem(errorMsg);
        }
    }

    [HttpPost]
    [Route("start")]
    public async Task<IActionResult> StartRunner()
    {

        if (!System.IO.File.Exists(_runnerPath))
        {
            var errorMsg = $"Runner not found at {_runnerPath}";
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "error", Message = errorMsg, Timestamp = DateTime.UtcNow });
            return NotFound(new { Error = errorMsg });
        }

        var runningProcesses = Process.GetProcessesByName(Constants.RunnerProcess);
        if (runningProcesses.Length > 0)
        {
            var warningMsg = $"Runner is already running (PIDs: {string.Join(", ", runningProcesses.Select(p => p.Id))})";
            _logger.LogWarning(warningMsg);
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "warning", Message = warningMsg, Timestamp = DateTime.UtcNow });
            return BadRequest(new { Error = warningMsg, RunningProcesses = runningProcesses.Select(p => p.Id) });
        }

        try
        {
            var result = await StartRunnerWithProject(_runnerPath, null, null);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        catch (Exception ex)
        {
            var errorMsg = $"Failed to start runner: {ex.Message}";
            _logger.LogError(ex, "Failed to start the runner");
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "error", Message = errorMsg, Timestamp = DateTime.UtcNow });
            return Problem(errorMsg);
        }
    }

    [HttpPost]
    [Route("stop")]
    public async Task<IActionResult> StopRunner([FromQuery] string? projectName = null)
    {
        try
        {
            var runningProcesses = Process.GetProcessesByName(Constants.RunnerProcess);

            if (runningProcesses.Length == 0)
            {
                var warningMsg = "No runner processes are currently running";
                await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "warning", Message = warningMsg, Timestamp = DateTime.UtcNow });
                return BadRequest(new { Error = warningMsg });
            }

            var stoppedProcesses = new List<StoppedProcessInfo>();

            if (!string.IsNullOrEmpty(projectName))
            {
                // Stop specific project
                var projectProcess = RunningProjects.Values.FirstOrDefault(p => p.ProjectName == projectName);
                if (projectProcess != null)
                {
                    var process = runningProcesses.FirstOrDefault(p => p.Id == projectProcess.ProcessId);
                    if (process != null)
                    {
                        await StopProcess(process, projectProcess);
                        stoppedProcesses.Add(new StoppedProcessInfo
                        {
                            ProcessId = process.Id,
                            ProjectName = projectName,
                            StoppedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    return NotFound(new { Error = $"Project '{projectName}' is not currently running" });
                }
            }
            else
            {
                // Stop all runner processes
                foreach (var process in runningProcesses)
                {
                    var projectInfo = RunningProjects.ContainsKey(process.Id) ? RunningProjects[process.Id] : null;
                    await StopProcess(process, projectInfo);
                    stoppedProcesses.Add(new StoppedProcessInfo
                    {
                        ProcessId = process.Id,
                        ProjectName = projectInfo?.ProjectName,
                        StoppedAt = DateTime.UtcNow
                    });
                }
            }

            var successMsg = projectName != null
                ? $"Stopped project '{projectName}'"
                : $"Stopped {stoppedProcesses.Count} runner process(es)";

            _logger.LogInformation(successMsg);
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new
            {
                Status = "stopped",
                Message = successMsg,
                StoppedProcesses = stoppedProcesses,
                Timestamp = DateTime.UtcNow
            });

            return Ok(new { Message = successMsg, StoppedProcesses = stoppedProcesses });
        }
        catch (Exception ex)
        {
            var errorMsg = $"Failed to stop runner: {ex.Message}";
            _logger.LogError(ex, "Failed to stop the runner");
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new { Status = "error", Message = errorMsg, Timestamp = DateTime.UtcNow });
            return Problem(errorMsg);
        }
    }

    [HttpGet]
    [Route("status")]
    public async Task<IActionResult> GetRunnerStatus()
    {
        try
        {
            var runnerPath = Environment.GetEnvironmentVariable(Constants.RunnerEnviromentVariableName) ?? Constants.RunnerLocation;
            var runningProcesses = Process.GetProcessesByName(Constants.RunnerProcess);

            var status = new
            {
                RunnerPath = runnerPath,
                RunnerExists = System.IO.File.Exists(runnerPath),
                IsRunning = runningProcesses.Length > 0,
                ProcessCount = runningProcesses.Length,
                RunningProjects = RunningProjects.Values.Select(p => new
                {
                    p.ProcessId,
                    p.ProjectName,
                    p.ProjectPath,
                    p.StartedAt,
                    RunningTime = DateTime.UtcNow - p.StartedAt,
                    ProcessExists = runningProcesses.Any(rp => rp.Id == p.ProcessId)
                }).ToArray(),
                Processes = runningProcesses.Select(p => new
                {
                    ProcessId = p.Id,
                    StartTime = p.StartTime,
                    WorkingSet = p.WorkingSet64,
                    FormattedMemory = FormatBytes(p.WorkingSet64),
                    HasMainWindow = p.MainWindowHandle != IntPtr.Zero,
                    WindowTitle = p.MainWindowTitle,
                    ProjectInfo = RunningProjects.ContainsKey(p.Id) ? RunningProjects[p.Id] : null
                }).ToArray(),
                Timestamp = DateTime.UtcNow
            };

            await _hubContext.Clients.All.SendAsync("RunnerStatusUpdate", status);

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get runner status");
            return Problem($"Failed to get status: {ex.Message}");
        }
    }

    [HttpGet]
    [Route("running-projects")]
    public async Task<IActionResult> GetRunningProjects()
    {
        try
        {
            var runningProcesses = Process.GetProcessesByName(Constants.RunnerProcess);
            var projects = new List<object>();

            foreach (var kvp in RunningProjects)
            {
                var processId = kvp.Key;
                var projectInfo = kvp.Value;
                var processExists = runningProcesses.Any(p => p.Id == processId);

                if (!processExists)
                {
                    // Clean up dead processes
                    RunningProjects.Remove(processId);
                    continue;
                }

                var process = runningProcesses.First(p => p.Id == processId);
                projects.Add(new
                {
                    projectInfo.ProcessId,
                    projectInfo.ProjectName,
                    projectInfo.ProjectPath,
                    projectInfo.StartedAt,
                    RunningTime = DateTime.UtcNow - projectInfo.StartedAt,
                    ProcessInfo = new
                    {
                        WorkingSet = process.WorkingSet64,
                        FormattedMemory = FormatBytes(process.WorkingSet64),
                        HasMainWindow = process.MainWindowHandle != IntPtr.Zero,
                        WindowTitle = process.MainWindowTitle
                    }
                });
            }

            return Ok(new { RunningProjects = projects, Count = projects.Count, Timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get running projects");
            return Problem($"Failed to get running projects: {ex.Message}");
        }
    }

    [HttpPost]
    [Route("kill-process")]
    public async Task<IActionResult> KillProcess([FromQuery] int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            var projectInfo = RunningProjects.ContainsKey(processId) ? RunningProjects[processId] : null;

            await StopProcess(process, projectInfo);

            var message = projectInfo != null
                ? $"Killed process {processId} running project '{projectInfo.ProjectName}'"
                : $"Killed process {processId}";

            await _hubContext.Clients.All.SendAsync("RunnerStatus", new
            {
                Status = "killed",
                Message = message,
                ProcessId = processId,
                ProjectName = projectInfo?.ProjectName,
                Timestamp = DateTime.UtcNow
            });

            return Ok(new { Message = message, ProcessId = processId });
        }
        catch (ArgumentException)
        {
            return NotFound(new { Error = $"Process {processId} not found" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kill process {ProcessId}", processId);
            return Problem($"Failed to kill process: {ex.Message}");
        }
    }

    // Private helper methods
    private async Task<RunnerStartResult> StartRunnerWithProject(string runnerPath, string? projectName, string? projectPath)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = runnerPath,
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = Path.GetDirectoryName(runnerPath),
                LoadUserProfile = true
            };

            if (!string.IsNullOrEmpty(projectPath))
            {
                processStartInfo.Arguments = $"\"{projectPath}\"";
            }

            var process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };

            // Monitor process exit
            process.Exited += async (sender, e) =>
            {
                if (sender is not Process exitedProcess) return;
                var exitCode = exitedProcess?.ExitCode ?? -1;
                var projectInfo = RunningProjects.TryGetValue(exitedProcess.Id, out var project) ? project : null;

                // Clean up tracking
                if (projectInfo != null)
                {
                    RunningProjects.Remove(exitedProcess.Id);
                }

                var exitMsg = projectInfo != null
                    ? $"Project '{projectInfo.ProjectName}' process exited with code: {exitCode}"
                    : $"Runner process exited with code: {exitCode}";

                _logger.LogInformation(exitMsg);
                await _hubContext.Clients.All.SendAsync("RunnerStatus", new
                {
                    Status = "exited",
                    Message = exitMsg,
                    ExitCode = exitCode,
                    ProcessId = exitedProcess.Id,
                    ProjectName = projectInfo?.ProjectName,
                    Timestamp = DateTime.UtcNow
                });
            };

            process.Start();

            // Track running project
            if (!string.IsNullOrEmpty(projectName))
            {
                RunningProjects[process.Id] = new RunningProjectInfo
                {
                    ProcessId = process.Id,
                    ProjectName = projectName,
                    ProjectPath = projectPath,
                    StartedAt = DateTime.UtcNow
                };
            }

            var successMsg = projectName != null
                    ? $"Started project '{projectName}' with PID: {process.Id}"
                    : $"Started runner with PID: {process.Id}";

            _logger.LogInformation(successMsg);
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new
            {
                Status = "started",
                Message = successMsg,
                ProcessId = process.Id,
                ProjectName = projectName,
                ProjectPath = projectPath,
                RunnerPath = runnerPath,
                Timestamp = DateTime.UtcNow
            });

            return new RunnerStartResult
            {
                Success = true,
                Message = successMsg,
                ProcessId = process.Id,
                ProjectName = projectName,
                RunnerPath = runnerPath
            };
        }
        catch (Exception ex)
        {
            var errorMsg = $"Failed to start runner: {ex.Message}";
            _logger.LogError(ex, errorMsg);
            await _hubContext.Clients.All.SendAsync("RunnerStatus", new
            {
                Status = "error",
                Message = errorMsg,
                ProjectName = projectName,
                Timestamp = DateTime.UtcNow
            });

            return new RunnerStartResult
            {
                Success = false,
                Error = errorMsg
            };
        }
    }

    private async Task StopProcess(Process process, RunningProjectInfo? projectInfo)
    {
        try
        {
            process.Kill();

            // Wait for process to exit with timeout
            if (!process.WaitForExit(5000))
            {
                _logger.LogWarning("Process {ProcessId} did not exit within timeout", process.Id);
            }

            // Clean up tracking
            if (projectInfo != null)
            {
                RunningProjects.Remove(process.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping process {ProcessId}", process.Id);
            throw;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;

        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:n1} {suffixes[counter]}";
    }
}

public class RunningProjectInfo
{
    public int ProcessId { get; set; }
    public string ProjectName { get; set; }
    public string ProjectPath { get; set; }
    public DateTime StartedAt { get; set; }
}

public class RunnerStartResult
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string Error { get; set; }
    public int? ProcessId { get; set; }
    public string ProjectName { get; set; }
    public string RunnerPath { get; set; }
}

public class StoppedProcessInfo
{
    public int ProcessId { get; set; }
    public string ProjectName { get; set; }
    public DateTime StoppedAt { get; set; }
}

public class TesaRunnerMessageHub : Hub
{

}