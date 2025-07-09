// IApiService.cs
using ProjectExporterMvvm.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using VersionsResponse = ProjectExporterMvvm.Views.VersionsResponse;

namespace ProjectExporter.Services
{
    public interface IApiService
    {
        string BaseUrl { get; set; }
        Task<bool> CheckConnectionAsync();
        Task<List<ProjectInfo>> GetProjectsAsync(bool groupByProject);
        Task<ProjectStatistics> GetStatisticsAsync();
        Task<RunnerStatusResponse> GetRunnerStatusAsync();
        Task<UploadSuccessResponse> UploadProjectAsync(string filePath, string version, string description, bool overrideExisting, Action<double> progressCallback);
        Task<bool> CheckVersionExistsAsync(string projectName, string version);
        Task<RunnerStartResponse> StartProjectAsync(string projectName);
        Task StopProjectAsync(string projectName);
        Task DeleteProjectAsync(string projectName, string version);
        Task<UploadSuccessResponse> CreateVersionAsync(string projectName, string version, string description, bool overrideExisting);
        Task<RunnerStartResponse> StartRunnerAsync();
        Task StopAllRunnersAsync();
        Task KillProcessAsync(int processId);
        Task CleanupProjectsAsync(int daysOld);
        Task<VersionsResponse> GetProjectVersionsAsync(string projectName);
    }
}



namespace ProjectExporter.Services
{
    public class ApiService : IApiService
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl;

        public string BaseUrl
        {
            get => _baseUrl;
            set => _baseUrl = value;
        }

        public ApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _baseUrl = "https://localhost:50000/api";
        }

        public async Task<bool> CheckConnectionAsync()
        {
            try
            {
                var projectHealthTask = _httpClient.GetAsync($"{_baseUrl}/project/health");
                var runnerHealthTask = _httpClient.GetAsync($"{_baseUrl}/runner/health");

                await Task.WhenAll(projectHealthTask, runnerHealthTask);

                var projectResponse = await projectHealthTask;
                var runnerResponse = await runnerHealthTask;

                return projectResponse.IsSuccessStatusCode && runnerResponse.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<ProjectInfo>> GetProjectsAsync(bool groupByProject)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/project/exported?groupByProject={groupByProject}");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ProjectResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result?.Projects ?? new List<ProjectInfo>();
        }

        public async Task<ProjectStatistics> GetStatisticsAsync()
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/project/stats");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ProjectStatistics>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        public async Task<RunnerStatusResponse> GetRunnerStatusAsync()
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/runner/status");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<RunnerStatusResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        public async Task<UploadSuccessResponse> UploadProjectAsync(string filePath, string version, string description, bool overrideExisting, Action<double> progressCallback)
        {
            using var content = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var streamContent = new StreamContent(fileStream);

            content.Add(streamContent, "formFile", Path.GetFileName(filePath));

            var url = $"{_baseUrl}/project/export";
            var queryParams = new List<string>();

            if (!string.IsNullOrEmpty(version))
                queryParams.Add($"version={Uri.EscapeDataString(version)}");
            if (!string.IsNullOrEmpty(description))
                queryParams.Add($"description={Uri.EscapeDataString(description)}");
            if (overrideExisting)
                queryParams.Add("overrideExisting=true");

            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams);

            var response = await _httpClient.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<UploadSuccessResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var conflict = JsonSerializer.Deserialize<VersionConflictResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                throw new VersionConflictException(conflict);
            }
            else
            {
                throw new Exception($"Upload failed ({response.StatusCode}): {responseContent}");
            }
        }

        public async Task<bool> CheckVersionExistsAsync(string projectName, string version)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/project/check-version?projectName={Uri.EscapeDataString(projectName)}&version={Uri.EscapeDataString(version)}");

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<VersionCheckResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return result?.Exists ?? false;
            }

            return false;
        }

        public async Task<RunnerStartResponse> StartProjectAsync(string projectName)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/runner/start-project?projectName={Uri.EscapeDataString(projectName)}", null);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<RunnerStartResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var error = JsonSerializer.Deserialize<RunnerErrorResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                throw new Exception(error?.Error ?? "Unknown error occurred");
            }
            else
            {
                throw new Exception($"HTTP {response.StatusCode}: {responseContent}");
            }
        }

        public async Task StopProjectAsync(string projectName)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/runner/stop?projectName={Uri.EscapeDataString(projectName)}", null);

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to stop project: {responseContent}");
            }
        }

        public async Task DeleteProjectAsync(string projectName, string version)
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/project/exported/{Uri.EscapeDataString(projectName)}?version={version}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception(errorContent);
            }
        }

        public async Task<UploadSuccessResponse> CreateVersionAsync(string projectName, string version, string description, bool overrideExisting)
        {
            var request = new
            {
                Version = version,
                Description = description,
                OverrideExisting = overrideExisting
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/project/exported/{Uri.EscapeDataString(projectName)}/new-version", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<UploadSuccessResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var conflict = JsonSerializer.Deserialize<VersionConflictResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                throw new VersionConflictException(conflict);
            }
            else
            {
                throw new Exception($"Failed to create version ({response.StatusCode}): {responseContent}");
            }
        }

        public async Task<RunnerStartResponse> StartRunnerAsync()
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/runner/start", null);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<RunnerStartResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            else
            {
                var error = JsonSerializer.Deserialize<RunnerErrorResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                throw new Exception(error?.Error ?? "Unknown error occurred");
            }
        }

        public async Task StopAllRunnersAsync()
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/runner/stop", null);

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to stop processes: {responseContent}");
            }
        }

        public async Task KillProcessAsync(int processId)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/runner/kill-process?processId={processId}", null);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to kill process: {errorContent}");
            }
        }

        public async Task CleanupProjectsAsync(int daysOld)
        {
            var response = await _httpClient.PostAsync($"{_baseUrl}/project/cleanup?daysOld={daysOld}", null);

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                throw new Exception(responseContent);
            }
        }

        public async Task<VersionsResponse> GetProjectVersionsAsync(string projectName)
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/project/exported/{Uri.EscapeDataString(projectName)}/versions");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<VersionsResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }

    public class VersionConflictException : Exception
    {
        public VersionConflictResponse ConflictResponse { get; }

        public VersionConflictException(VersionConflictResponse conflictResponse)
            : base(conflictResponse.Message)
        {
            ConflictResponse = conflictResponse;
        }
    }
}



namespace ProjectExporter.Services
{
    public interface IDialogService
    {
        Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton button, MessageBoxImage icon);
        Task<(bool Confirmed, bool ShouldOverride, string NewVersion)> ShowVersionConflictAsync(VersionConflictResponse conflict);
        Task<(bool Confirmed, string Version, string Description)> ShowCreateVersionAsync(ProjectInfo project);
        (bool Confirmed, string SelectedServer) ShowServerSettings(string currentServer, List<string> recentServers);
        void ShowVersionsWindow(ProjectInfo project);
        void ShowQuickConnectMenu(object placementTarget, (string Label, string Url)[] options, System.Action<string> onSelected);
    }
}

// IFileService.cs
namespace ProjectExporter.Services
{
    public interface IFileService
    {
        void OpenFolder(string path);
    }
}

// FileService.cs

namespace ProjectExporter.Services
{
    public class FileService : IFileService
    {
        public void OpenFolder(string path)
        {
            Process.Start("explorer.exe", path);
        }
    }
}