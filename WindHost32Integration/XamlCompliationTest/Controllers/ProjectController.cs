using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TESA.ModelLibrary.Project.ServiceModels;

namespace TESA.Runner.ProjectService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ProjectController : ControllerBase
{
    private readonly ILogger<ProjectController> _logger;
    private readonly Services.ProjectService _projectService;

    public ProjectController(ILogger<ProjectController> logger, Services.ProjectService projectService)
    {
        _logger = logger;
        _projectService = projectService;
    }
    [HttpGet]
    [Route("health")]
    public async Task<IActionResult> GetHealth()
    {
        try
        {
            var exportedProjects = await _projectService.GetExportedProjects();
            var projectCount = exportedProjects.Count();

            return Ok(new
            {
                Status = "Healthy",
                Message = "Project service is operational",
                ProjectCount = projectCount,
                ConnectionInfo = new
                {
                    RemoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                    LocalIpAddress = HttpContext.Connection.LocalIpAddress?.ToString(),
                    UserAgent = HttpContext.Request.Headers["User-Agent"].ToString()
                },
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Health check failed");
            return StatusCode(500, new
            {
                Status = "Unhealthy",
                Message = "Project service encountered an error",
                Error = e.Message,
                Timestamp = DateTime.UtcNow
            });
        }
    }
    [HttpPost]
    [Route("export")]
    public async Task<IActionResult> ExportProject(
        IFormFile? formFile,
        [FromQuery] string version,
        [FromQuery] string? description = null,
        [FromQuery] bool overrideExisting = false)
    {
        if (formFile == null || formFile.Length == 0)
        {
            return BadRequest(new { Error = "No file uploaded", Message = "Please select a file to upload" });
        }

        try
        {
            _logger.LogInformation("Processing file upload: {FileName}, Size: {FileSize} bytes", formFile.FileName, formFile.Length);

            var result = await _projectService.ExportProject(
                formFile,
                version,
                description,
                User?.Identity?.Name ?? "Anonymous",
                overrideExisting
            );

            if (!result.Success)
            {
                if (result.RequiresOverrideConfirmation)
                {
                    return Conflict(new
                    {
                        Error = "Version conflict",
                        Message = result.Message,
                        ConflictingVersion = result.ConflictingVersion,
                        ConflictingProjectName = result.ConflictingProjectName,
                        ExistingProject = result.ExistingProjectInfo,
                        RequiresOverrideConfirmation = true,
                        SuggestedVersions = await _projectService.GetVersionSuggestions(
                            result.ConflictingProjectName,
                            result.ConflictingVersion
                        )
                    });
                }

                return BadRequest(new { Error = "Export failed", Message = result.Message });
            }

            return Ok(new
            {
                Message = result.Message,
                ProjectName = result.ProjectName,
                Version = result.Version,
                WasOverridden = result.WasOverridden,
                FileName = formFile.FileName,
                Size = formFile.Length,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing uploaded archive: {FileName}", formFile?.FileName);
            return StatusCode(500, new
            {
                Error = "Processing failed",
                Message = "An error occurred while processing the uploaded archive",
                Details = ex.Message
            });
        }
    }

    [HttpPost]
    [Route("exported/{projectName}/new-version")]
    public async Task<IActionResult> CreateNewVersion(
        string projectName,
        [FromBody] CreateVersionRequest? request)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new { Error = "Invalid request", Message = "Request body is required" });
            }

            var result = await _projectService.CreateNewVersion(
                projectName,
                request.Version,
                request.Description,
                User?.Identity?.Name ?? "Anonymous",
                request.OverrideExisting
            );

            if (!result.Success)
            {
                if (result.RequiresOverrideConfirmation)
                {
                    return Conflict(new
                    {
                        Error = "Version conflict",
                        Message = result.Message,
                        ConflictingVersion = result.ConflictingVersion,
                        ConflictingProjectName = result.ConflictingProjectName,
                        ExistingProject = result.ExistingProjectInfo,
                        RequiresOverrideConfirmation = true,
                        SuggestedVersions = await _projectService.GetVersionSuggestions(
                            result.ConflictingProjectName,
                            result.ConflictingVersion
                        )
                    });
                }

                return BadRequest(new { Error = "Create version failed", Message = result.Message });
            }

            return Ok(new
            {
                Message = result.Message,
                OriginalProject = projectName,
                NewProjectName = result.ProjectName,
                Version = result.Version,
                WasOverridden = result.WasOverridden,
                Description = request.Description,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new version for project: {ProjectName}", projectName);
            return StatusCode(500, new { Error = "Failed to create new version", Message = ex.Message });
        }
    }

    [HttpGet]
    [Route("check-version")]
    public async Task<IActionResult> CheckVersionExists([FromQuery] string projectName, [FromQuery] string version)
    {
        try
        {
            var exists = await _projectService.VersionExists(projectName, version);
            var suggestions = exists ? await _projectService.GetVersionSuggestions(projectName, version) : new List<string>();

            return Ok(new
            {
                ProjectName = projectName,
                Version = version,
                Exists = exists,
                SuggestedVersions = suggestions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking version existence");
            return StatusCode(500, new { Error = "Version check failed", Message = ex.Message });
        }
    }

    [HttpGet]
    [Route("exported")]
    public async Task<ActionResult<IEnumerable<ProjectInfo>>> GetExportedProjects([FromQuery] bool groupByProject = false)
    {
        try
        {
            var projects = await _projectService.GetExportedProjects(groupByProject);

            return Ok(new
            {
                Projects = projects,
                Count = projects.Count(),
                GroupedByProject = groupByProject,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving exported projects");
            return StatusCode(500, new { Error = "Failed to retrieve projects", Message = ex.Message });
        }
    }

    [HttpGet]
    [Route("exported/{projectName}")]
    public async Task<ActionResult<ProjectInfo>> GetProject(string projectName, [FromQuery] string? version = null)
    {
        try
        {
            var project = await _projectService.GetProjectByNameAndVersion(projectName, version);

            if (project == null)
            {
                return NotFound(new
                {
                    Error = "Project not found",
                    Message = $"Project '{projectName}' {(version != null ? $"version '{version}'" : "")} was not found"
                });
            }

            return Ok(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving project: {ProjectName}, Version: {Version}", projectName, version);
            return StatusCode(500, new { Error = "Failed to retrieve project", Message = ex.Message });
        }
    }

    [HttpGet]
    [Route("exported/{projectName}/versions")]
    public async Task<ActionResult<IEnumerable<ProjectInfo>>> GetProjectVersions(string projectName)
    {
        try
        {
            var baseProjectName = ExtractBaseProjectName(projectName);
            var versions = (await _projectService.GetExportedProjects())
                .Where(p => p.BaseProjectName == baseProjectName)
                .OrderByDescending(p => p.VersionNumber)
                .ToList();

            if (!versions.Any())
            {
                return NotFound(new { Error = "Project not found", Message = $"No versions found for project '{baseProjectName}'" });
            }

            return Ok(new
            {
                ProjectName = baseProjectName,
                Versions = versions,
                LatestVersion = versions.First().Version,
                VersionCount = versions.Count,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving project versions: {ProjectName}", projectName);
            return StatusCode(500, new { Error = "Failed to retrieve project versions", Message = ex.Message });
        }
    }

    [HttpDelete]
    [Route("exported/{projectName}")]
    public async Task<IActionResult> DeleteProject(string projectName, [FromQuery] string? version = null)
    {
        try
        {
            var success = await _projectService.DeleteProjectVersion(projectName, version);

            if (!success)
            {
                return NotFound(new
                {
                    Error = "Project not found",
                    Message = $"Project '{projectName}' {(version != null ? $"version '{version}'" : "")} was not found"
                });
            }

            return Ok(new
            {
                Message = "Project deleted successfully",
                ProjectName = projectName,
                Version = version ?? "latest",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting project: {ProjectName}, Version: {Version}", projectName, version);
            return StatusCode(500, new { Error = "Failed to delete project", Message = ex.Message });
        }
    }

    [HttpGet]
    [Route("stats")]
    public async Task<ActionResult> GetProjectStatistics()
    {
        try
        {
            var projects = (await _projectService.GetExportedProjects()).ToList();

            var stats = new
            {
                TotalProjects = projects.Count,
                UniqueProjects = projects.Select(p => p.BaseProjectName).Distinct().Count(),
                TotalSizeBytes = projects.Sum(p => p.SizeInBytes),
                FormattedTotalSize = FormatBytes(projects.Sum(p => p.SizeInBytes)),
                TotalFiles = projects.Sum(p => p.FileCount),
                AverageProjectSize = projects.Any() ? projects.Average(p => p.SizeInBytes) : 0,
                LatestProject = projects.OrderByDescending(p => p.LastModifiedDate).FirstOrDefault()?.Name,
                OldestProject = projects.OrderBy(p => p.CreatedDate).FirstOrDefault()?.Name,
                ProjectsByStatus = projects.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count()),
                Timestamp = DateTime.UtcNow
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating project statistics");
            return StatusCode(500, new { Error = "Failed to generate statistics", Message = ex.Message });
        }
    }

    [HttpPost]
    [Route("cleanup")]
    public async Task<IActionResult> CleanupOldProjects([FromQuery] int daysOld = 30)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysOld);
            var projects = (await _projectService.GetExportedProjects())
                .Where(p => p.LastModifiedDate < cutoffDate)
                .ToList();

            var deletedProjects = new List<string>();
            foreach (var project in projects)
            {
                var success = await _projectService.DeleteProjectVersion(project.Name);
                if (success)
                {
                    deletedProjects.Add(project.Name);
                }
            }

            return Ok(new
            {
                Message = $"Cleanup completed. Deleted projects older than {daysOld} days",
                DeletedCount = deletedProjects.Count,
                DeletedProjects = deletedProjects,
                CutoffDate = cutoffDate,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during project cleanup");
            return StatusCode(500, new { Error = "Cleanup failed", Message = ex.Message });
        }
    }

    [HttpGet]
    [Route("download")]
    public async Task<IActionResult> DownloadProject([FromBody] ProjectInfo projectInfo)
    {
        try
        {
            var archiveResult = await _projectService.GetProjectArchive(projectInfo.Name, projectInfo.Version);
            if (archiveResult is null)
            {
                return NotFound(new
                {
                    Error = "Project not found",
                    Message = $"Project '{projectInfo.Name}' {(projectInfo.Version != null ? $"version '{projectInfo.Version}'" : "")} was not found"
                });
            }

            return File(archiveResult.Stream, archiveResult.ContentType, archiveResult.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending project archive: {ProjectName} v{Version}", projectInfo.Name, projectInfo.Version);
            return StatusCode(500, new { Error = "Failed to create project archive", Message = ex.Message });
        }
    }

    // Helper methods
    private string ExtractBaseProjectName(string fullProjectName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fullProjectName, @"^(.+?)(_v[\d.]+.*)?$");
        return match.Groups[1].Value;
    }

    private string FormatBytes(long bytes)
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
