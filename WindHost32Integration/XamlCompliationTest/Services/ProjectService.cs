using Microsoft.AspNetCore.Http;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TESA.ModelLibrary.Project.ServiceModels;

namespace TESA.Runner.ProjectService.Services
{

    public class ProjectService
    {
        private readonly ILogger<ProjectService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _exportedProjectsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExportedProjects");
        private const string MetadataFileName = "project-metadata.json";

        public ProjectService(ILogger<ProjectService> logger, IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        // Enhanced export with version conflict handling
        public async Task<ExportResult> ExportProject(
            IFormFile? formFile,
            string? version = null,
            string? description = null,
            string? createdBy = null,
            bool overrideExisting = false)
        {
            if (formFile is null || formFile.Length is 0)
            {
                _logger.LogWarning("No file uploaded.");
                throw new ArgumentException("No file uploaded");
            }

            try
            {
                _logger.LogInformation("Received file: {FileName}, Size: {FileSize} bytes", formFile.FileName, formFile.Length);
                Directory.CreateDirectory(_exportedProjectsDirectory);

                var baseProjectName = Path.GetFileNameWithoutExtension(formFile.FileName);
                var projectVersion = version ?? await DetermineNextVersion(baseProjectName);

                // Check for version conflict
                var existingProject = await GetProjectByNameAndVersion(baseProjectName, projectVersion);
                if (existingProject != null && !overrideExisting)
                {
                    return new ExportResult
                    {
                        Success = false,
                        RequiresOverrideConfirmation = true,
                        ConflictingVersion = projectVersion,
                        ConflictingProjectName = baseProjectName,
                        ExistingProjectInfo = existingProject,
                        Message = $"Version {projectVersion} already exists for project '{baseProjectName}'"
                    };
                }

                // Create temp file
                var tempZipPath = Path.Combine(Path.GetTempPath(), formFile.FileName);
                await using (var tempStream = new FileStream(tempZipPath, FileMode.Create))
                {
                    await formFile.CopyToAsync(tempStream);
                }

                var targetDirectory = CreateVersionedDirectory(baseProjectName, projectVersion, overrideExisting);
                var requesterIp = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown IP";

                // If overriding, delete existing directory first
                if (overrideExisting && existingProject != null)
                {
                    _logger.LogInformation("Overriding existing version {Version} for project {ProjectName}", projectVersion, baseProjectName);
                    Directory.Delete(existingProject.FullPath, true);
                }

                // Extract the project
                ZipFile.ExtractToDirectory(tempZipPath, targetDirectory, true);

                // Create/update metadata
                var metadata = new ProjectMetadata
                {
                    ProjectName = baseProjectName,
                    Version = projectVersion,
                    Description = description ?? $"Imported from: {requesterIp}",
                    CreatedDate = existingProject?.CreatedDate ?? DateTime.UtcNow,
                    LastModifiedDate = DateTime.UtcNow,
                    CreatedBy = existingProject?.Metadata?.CreatedBy ?? createdBy ?? "System",
                    ModifiedBy = overrideExisting ? (createdBy ?? "System") : null,
                    ChangeLog = overrideExisting
                        ? $"Project overridden from {formFile.FileName} by {createdBy ?? "System"}"
                        : $"Project imported from {formFile.FileName}"
                };

                await SaveProjectMetadata(targetDirectory, metadata);

                // Clean up temp file
                File.Delete(tempZipPath);

                _logger.LogInformation("Project exported successfully to: {TargetDirectory} with version: {Version}", targetDirectory, projectVersion);

                return new ExportResult
                {
                    Success = true,
                    ProjectName = Path.GetFileName(targetDirectory),
                    Version = projectVersion,
                    WasOverridden = overrideExisting,
                    Message = overrideExisting
                        ? $"Project {baseProjectName} v{projectVersion} was successfully overridden"
                        : $"Project {baseProjectName} v{projectVersion} was successfully created"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting project from file: {FileName}", formFile?.FileName);
                return new ExportResult
                {
                    Success = false,
                    Message = $"Export failed: {ex.Message}",
                    Error = ex
                };
            }
        }

        // Enhanced create new version with conflict handling
        public async Task<ExportResult> CreateNewVersion(
            string projectName,
            string? newVersion = null,
            string? description = null,
            string? modifiedBy = null,
            bool overrideExisting = false)
        {
            try
            {
                var baseProjectName = ExtractBaseProjectName(projectName);
                var latestProject = GetLatestProjectVersion(baseProjectName);

                if (latestProject == null)
                {
                    return new ExportResult
                    {
                        Success = false,
                        Message = $"Base project '{baseProjectName}' not found"
                    };
                }

                var targetVersion = newVersion ?? await DetermineNextVersion(baseProjectName);

                // Check for version conflict
                var existingProject = await GetProjectByNameAndVersion(baseProjectName, targetVersion);
                if (existingProject != null && !overrideExisting)
                {
                    return new ExportResult
                    {
                        Success = false,
                        RequiresOverrideConfirmation = true,
                        ConflictingVersion = targetVersion,
                        ConflictingProjectName = baseProjectName,
                        ExistingProjectInfo = existingProject,
                        Message = $"Version {targetVersion} already exists for project '{baseProjectName}'"
                    };
                }

                var newDirectory = CreateVersionedDirectory(baseProjectName, targetVersion, overrideExisting);

                // If overriding, delete existing directory first
                if (overrideExisting && existingProject != null)
                {
                    _logger.LogInformation("Overriding existing version {Version} for project {ProjectName}", targetVersion, baseProjectName);
                    Directory.Delete(existingProject.FullPath, true);
                }

                // Copy the latest version to new directory
                CopyDirectory(latestProject.FullPath, newDirectory);

                // Update metadata
                var existingMetadata = await LoadProjectMetadata(latestProject.FullPath);
                var newMetadata = new ProjectMetadata
                {
                    ProjectName = baseProjectName,
                    Version = targetVersion,
                    Description = description ?? $"New version based on {latestProject.Version}",
                    CreatedDate = existingProject?.CreatedDate ?? DateTime.UtcNow,
                    LastModifiedDate = DateTime.UtcNow,
                    CreatedBy = existingProject?.Metadata?.CreatedBy ?? existingMetadata?.CreatedBy ?? "System",
                    ModifiedBy = modifiedBy ?? "System",
                    CustomProperties = existingMetadata?.CustomProperties ?? new Dictionary<string, string>(),
                    Tags = existingMetadata?.Tags ?? new List<string>(),
                    ChangeLog = overrideExisting
                        ? $"Version {targetVersion} overridden from {latestProject.Version} by {modifiedBy ?? "System"}"
                        : $"Version {targetVersion} created from {latestProject.Version}"
                };

                await SaveProjectMetadata(newDirectory, newMetadata);

                _logger.LogInformation("Created new version {Version} for project {ProjectName}", targetVersion, baseProjectName);

                return new ExportResult
                {
                    Success = true,
                    ProjectName = Path.GetFileName(newDirectory),
                    Version = targetVersion,
                    WasOverridden = overrideExisting,
                    Message = overrideExisting
                        ? $"Version {targetVersion} was successfully overridden"
                        : $"Version {targetVersion} was successfully created"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating new version for project: {ProjectName}", projectName);
                return new ExportResult
                {
                    Success = false,
                    Message = $"Failed to create new version: {ex.Message}",
                    Error = ex
                };
            }
        }

        // Check if version exists
        public async Task<bool> VersionExists(string baseProjectName, string version)
        {
            var existingProject = await GetProjectByNameAndVersion(baseProjectName, version);
            return existingProject != null;
        }

        // Get version suggestions to avoid conflicts
        public async Task<List<string>> GetVersionSuggestions(string baseProjectName, string? desiredVersion)
        {
            var suggestions = new List<string>();

            // Add the next auto-increment version
            var nextVersion = await DetermineNextVersion(baseProjectName);
            suggestions.Add(nextVersion);

            // Add variations of the desired version
            if (!string.IsNullOrEmpty(desiredVersion) && Version.TryParse(desiredVersion, out var version))
            {
                // Increment build number
                suggestions.Add(new Version(version.Major, version.Minor, version.Build + 1).ToString());

                // Increment minor version
                suggestions.Add(new Version(version.Major, version.Minor + 1, 0).ToString());

                // Increment major version
                suggestions.Add(new Version(version.Major + 1, 0, 0).ToString());
            }

            return suggestions.Distinct().ToList();
        }

        // Rest of the existing methods with small modifications...
        private string CreateVersionedDirectory(string baseProjectName, string version, bool overrideExisting = false)
        {
            var directoryName = $"{baseProjectName}_v{version}";
            var targetDirectory = Path.Combine(_exportedProjectsDirectory, directoryName);

            if (!overrideExisting)
            {
                // Ensure directory doesn't exist (old behavior for backwards compatibility)
                int counter = 1;
                while (Directory.Exists(targetDirectory))
                {
                    directoryName = $"{baseProjectName}_v{version}_{counter}";
                    targetDirectory = Path.Combine(_exportedProjectsDirectory, directoryName);
                    counter++;
                }
            }

            return targetDirectory;
        }

        // [Include all other existing methods unchanged...]
        public async Task<IEnumerable<ProjectInfo>> GetExportedProjects(bool groupByProject = false)
        {
            try
            {
                if (!Directory.Exists(_exportedProjectsDirectory))
                {
                    _logger.LogWarning("Exported projects directory does not exist: {Directory} Creating Folder", _exportedProjectsDirectory);
                    Directory.CreateDirectory(_exportedProjectsDirectory);
                    return Enumerable.Empty<ProjectInfo>();
                }

                _logger.LogInformation("Retrieving exported projects from directory: {Directory}", _exportedProjectsDirectory);

                var directories = Directory.GetDirectories(_exportedProjectsDirectory);
                var projectInfos = new List<ProjectInfo>();

                foreach (var directory in directories)
                {
                    try
                    {
                        var projectInfo = await CreateProjectInfo(directory);
                        projectInfos.Add(projectInfo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error processing directory: {Directory}", directory);

                        // Still add the project but with limited info
                        projectInfos.Add(new ProjectInfo
                        {
                            Name = Path.GetFileName(directory),
                            FullPath = directory,
                            Status = "Error",
                            CreatedDate = DateTime.MinValue,
                            LastModifiedDate = DateTime.MinValue,
                            LastAccessedDate = DateTime.MinValue
                        });
                    }
                }

                if (groupByProject)
                {
                    // Group by base project name and add available versions
                    var grouped = projectInfos.GroupBy(p => p.BaseProjectName).ToList();
                    var result = new List<ProjectInfo>();

                    foreach (var group in grouped)
                    {
                        var latest = group.OrderByDescending(p => p.VersionNumber).First();
                        latest.AvailableVersions = group.Select(p => p.Version).OrderByDescending(v => v).ToList();
                        result.Add(latest);
                    }

                    return result.OrderByDescending(p => p.LastModifiedDate);
                }

                // Sort by last modified date (newest first)
                return projectInfos.OrderByDescending(p => p.LastModifiedDate);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error getting exported projects from directory: {Directory}", _exportedProjectsDirectory);
                return Enumerable.Empty<ProjectInfo>();
            }
        }

        public async Task<ProjectInfo?> GetProjectByNameAndVersion(string projectName, string? version = null)
        {
            var baseProjectName = ExtractBaseProjectName(projectName);
            var projects = (await GetExportedProjects()).Where(p => p.BaseProjectName == baseProjectName).ToList();

            if (!projects.Any())
            {
                return null;
            }

            if (string.IsNullOrEmpty(version))
            {
                // Return latest version
                return projects.OrderByDescending(p => p.VersionNumber).First();
            }

            return projects.FirstOrDefault(p => p.Version == version);
        }

        public async Task<bool> DeleteProjectVersion(string projectName, string? version = null)
        {
            try
            {
                var project = await GetProjectByNameAndVersion(projectName, version);
                if (project == null)
                {
                    return false;
                }

                Directory.Delete(project.FullPath, true);
                _logger.LogInformation("Deleted project version: {ProjectName} v{Version}", projectName, project.Version);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting project version: {ProjectName} v{Version}", projectName, version);
                return false;
            }
        }

        private async Task<ProjectInfo> CreateProjectInfo(string directory)
        {
            var dirInfo = new DirectoryInfo(directory);
            var metadata = await LoadProjectMetadata(directory);

            var projectInfo = new ProjectInfo
            {
                Name = dirInfo.Name,
                FullPath = directory,
                CreatedDate = metadata?.CreatedDate ?? dirInfo.CreationTime,
                LastModifiedDate = metadata?.LastModifiedDate ?? dirInfo.LastWriteTime,
                LastAccessedDate = dirInfo.LastAccessTime,
                Status = "Available",
                Version = metadata?.Version ?? "1.0.0",
                BaseProjectName = metadata?.ProjectName ?? ExtractBaseProjectName(dirInfo.Name),
                Metadata = metadata ?? new ProjectMetadata()
            };

            // Extract version number for sorting
            if (Version.TryParse(projectInfo.Version, out var version))
            {
                projectInfo.VersionNumber = version.Major * 10000 + version.Minor * 100 + version.Build;
            }

            // Calculate directory size and file count
            var (size, fileCount) = CalculateDirectorySize(directory);
            projectInfo.SizeInBytes = size;
            projectInfo.FormattedSize = FormatBytes(size);
            projectInfo.FileCount = fileCount;

            return projectInfo;
        }

        private async Task<string> DetermineNextVersion(string baseProjectName)
        {
            var existingProjects = (await GetExportedProjects())
                .Where(p => p.BaseProjectName == baseProjectName)
                .ToList();

            if (!existingProjects.Any())
            {
                return "1.0.0";
            }

            var latestVersion = existingProjects
                .OrderByDescending(p => p.VersionNumber)
                .First().Version;

            if (Version.TryParse(latestVersion, out var version))
            {
                var newVersion = new Version(version.Major, version.Minor, version.Build + 1);
                return newVersion.ToString();
            }

            return "1.0.1";
        }

        private string ExtractBaseProjectName(string fullProjectName)
        {
            // Remove version suffix if present (e.g., "MyProject_v1.0.0" -> "MyProject")
            var match = System.Text.RegularExpressions.Regex.Match(fullProjectName, @"^(.+?)(_v[\d.]+.*)?$");
            return match.Groups[1].Value;
        }

        private ProjectInfo? GetLatestProjectVersion(string baseProjectName)
        {
            return GetExportedProjects().Result
                .Where(p => p.BaseProjectName == baseProjectName)
                .OrderByDescending(p => p.VersionNumber)
                .FirstOrDefault();
        }

        private async Task SaveProjectMetadata(string projectDirectory, ProjectMetadata metadata)
        {
            var metadataPath = Path.Combine(projectDirectory, MetadataFileName);
            var jsonContent = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, jsonContent);
        }

        private async Task<ProjectMetadata?> LoadProjectMetadata(string projectDirectory)
        {
            var metadataPath = Path.Combine(projectDirectory, MetadataFileName);

            if (!File.Exists(metadataPath))
            {
                return null;
            }

            try
            {
                var jsonContent = await File.ReadAllTextAsync(metadataPath);
                return JsonSerializer.Deserialize<ProjectMetadata>(jsonContent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load metadata from: {MetadataPath}", metadataPath);
                return null;
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(sourceDir, file);
                var destFile = Path.Combine(destDir, relativePath);
                var dirName = Path.GetDirectoryName(destFile);
                if (dirName is null)
                {
                    throw new InvalidOperationException("Destination file path cannot be null.");
                }
                Directory.CreateDirectory(dirName);
                File.Copy(file, destFile, true);
            }
        }

        private (long size, int fileCount) CalculateDirectorySize(string directoryPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);

                long totalSize = files.Sum(file => file.Length);
                int fileCount = files.Length;

                return (totalSize, fileCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not calculate size for directory: {Directory}", directoryPath);
                return (0, 0);
            }
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
        // Creates a zip archive for the specified project (optionally version) and returns a stream for download
        public async Task<ProjectArchiveResult?> GetProjectArchive(string projectName, string? version = null)
        {
            var project = await GetProjectByNameAndVersion(projectName, version);
            if (project == null)
            {
                return null;
            }

            try
            {
                var fileName = $"{project.BaseProjectName}_v{project.Version}.zip";
                var tempZipPath = Path.Combine(Path.GetTempPath(), fileName);

                // Ensure any stale temp archive is removed first
                if (File.Exists(tempZipPath))
                {
                    try { File.Delete(tempZipPath); } catch { /* ignore file in use */ }
                }

                // Re-create the archive from the existing project directory
                ZipFile.CreateFromDirectory(project.FullPath, tempZipPath, CompressionLevel.Fastest, includeBaseDirectory: false);

                // Open a read-only stream that will delete the temp file when disposed
                var stream = new FileStream(tempZipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);

                return new ProjectArchiveResult(stream, fileName, "application/zip");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create archive for project {ProjectName} v{Version}", projectName, version ?? "latest");
                return null;
            }
        }
    }

    /// <summary>
    /// Represents the result of building a project archive for download.
    /// </summary>
    public record ProjectArchiveResult(Stream Stream, string FileName, string ContentType);
}
