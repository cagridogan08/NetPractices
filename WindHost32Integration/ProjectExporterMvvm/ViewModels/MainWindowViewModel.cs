using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ProjectExporter.Services;
using ProjectExporterMvvm.Models;

namespace ProjectExporterMvvm.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IApiService _apiService;
        private readonly IDialogService _dialogService;
        private readonly string _settingsFilePath;

        [ObservableProperty]
        private string _apiBaseUrl = "https://localhost:50000/api";

        [ObservableProperty]
        private ObservableCollection<ProjectInfo> _projects = new();

        [ObservableProperty]
        private ObservableCollection<ActivityLog> _recentActivity = new();

        [ObservableProperty]
        private ObservableCollection<RunningProjectInfo> _runningProjects = new();

        [ObservableProperty]
        private ProjectInfo _selectedProject;

        [ObservableProperty]
        private string _selectedFilePath;

        [ObservableProperty]
        private string _filePathText = "No file selected...";

        [ObservableProperty]
        private string _versionText = "";

        [ObservableProperty]
        private string _descriptionText = "";

        [ObservableProperty]
        private string _statusText = "Connecting...";

        [ObservableProperty]
        private string _statusBarText = "Ready";

        [ObservableProperty]
        private string _connectionStatusText = "API: Connecting...";

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isUploadEnabled;

        [ObservableProperty]
        private bool _isCreateVersionEnabled;

        [ObservableProperty]
        private bool _isOpenFolderEnabled;

        [ObservableProperty]
        private double _uploadProgress;

        [ObservableProperty]
        private string _uploadStatusText = "";

        [ObservableProperty]
        private bool _isUploadIndeterminate;

        [ObservableProperty]
        private bool _groupByProject;

        [ObservableProperty]
        private string _searchText = "";

        [ObservableProperty]
        private string _versionValidationText = "";

        [ObservableProperty]
        private string _runnerStatusText = "Status: Unknown";

        [ObservableProperty]
        private string _runnerPathText = "Path: Unknown";

        [ObservableProperty]
        private ProjectStatistics _statistics;

        [ObservableProperty]
        private ObservableCollection<string> _recentServers = new();

        public ObservableCollection<ProjectInfo> FilteredProjects => GetFilteredProjects();

        public MainViewModel(IApiService apiService, IDialogService dialogService)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            _settingsFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ProjectExporter",
                "settings.json");

            InitializeCommands();
        }

        private void InitializeCommands()
        {
            // File operations
            BrowseCommand = new AsyncRelayCommand(BrowseFileAsync);
            UploadCommand = new AsyncRelayCommand(UploadFileAsync, () => IsUploadEnabled);

            // Project operations
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            RunProjectCommand = new AsyncRelayCommand<ProjectInfo>(RunProjectAsync);
            StopProjectCommand = new AsyncRelayCommand<ProjectInfo>(StopProjectAsync);
            DeleteProjectCommand = new AsyncRelayCommand<ProjectInfo>(DeleteProjectAsync);
            ViewVersionsCommand = new RelayCommand<ProjectInfo>(ViewVersions);
            CreateVersionCommand = new AsyncRelayCommand(CreateVersionAsync, () => IsCreateVersionEnabled);
            OpenFolderCommand = new RelayCommand(OpenFolder, () => IsOpenFolderEnabled);

            // Runner operations
            StartRunnerCommand = new AsyncRelayCommand(StartRunnerAsync);
            StopAllRunnersCommand = new AsyncRelayCommand(StopAllRunnersAsync);
            RefreshRunnerStatusCommand = new AsyncRelayCommand(LoadRunnerStatusAsync);
            StopRunningProjectCommand = new AsyncRelayCommand<RunningProjectInfo>(StopRunningProjectAsync);
            KillProcessCommand = new AsyncRelayCommand<RunningProjectInfo>(KillProcessAsync);

            // Settings operations
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
            UpdateServerUrlCommand = new AsyncRelayCommand<string>(UpdateServerUrlAsync);
            ShowSettingsCommand = new RelayCommand(ShowSettings);
            ShowQuickConnectCommand = new RelayCommand<object>(ShowQuickConnect);

            // Cleanup
            CleanupCommand = new AsyncRelayCommand(CleanupAsync);
        }

        #region Commands

        public IAsyncRelayCommand BrowseCommand { get; private set; }
        public IAsyncRelayCommand UploadCommand { get; private set; }
        public IAsyncRelayCommand RefreshCommand { get; private set; }
        public IAsyncRelayCommand<ProjectInfo> RunProjectCommand { get; private set; }
        public IAsyncRelayCommand<ProjectInfo> StopProjectCommand { get; private set; }
        public IAsyncRelayCommand<ProjectInfo> DeleteProjectCommand { get; private set; }
        public IRelayCommand<ProjectInfo> ViewVersionsCommand { get; private set; }
        public IAsyncRelayCommand CreateVersionCommand { get; private set; }
        public IRelayCommand OpenFolderCommand { get; private set; }
        public IAsyncRelayCommand StartRunnerCommand { get; private set; }
        public IAsyncRelayCommand StopAllRunnersCommand { get; private set; }
        public IAsyncRelayCommand RefreshRunnerStatusCommand { get; private set; }
        public IAsyncRelayCommand<RunningProjectInfo> StopRunningProjectCommand { get; private set; }
        public IAsyncRelayCommand<RunningProjectInfo> KillProcessCommand { get; private set; }
        public IAsyncRelayCommand TestConnectionCommand { get; private set; }
        public IAsyncRelayCommand<string> UpdateServerUrlCommand { get; private set; }
        public IRelayCommand ShowSettingsCommand { get; private set; }
        public IRelayCommand<object> ShowQuickConnectCommand { get; private set; }
        public IAsyncRelayCommand CleanupCommand { get; private set; }

        #endregion

        public async Task InitializeAsync()
        {
            LoadSettings();
            await CheckApiConnectionAsync();
            await LoadProjectsAsync();
            await LoadStatisticsAsync();
            await LoadRunnerStatusAsync();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);

                    if (settings != null)
                    {
                        ApiBaseUrl = settings.ApiBaseUrl ?? ApiBaseUrl;

                        RecentServers.Clear();
                        foreach (var server in settings.RecentServers ?? new List<string>())
                        {
                            if (!string.IsNullOrWhiteSpace(server))
                            {
                                RecentServers.Add(server);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddActivity("⚠️", $"Failed to load settings: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                var settingsDir = Path.GetDirectoryName(_settingsFilePath);
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                var recentServers = new List<string>();
                if (!string.IsNullOrWhiteSpace(ApiBaseUrl))
                {
                    recentServers.Add(ApiBaseUrl);
                }

                recentServers.AddRange(RecentServers.Where(s => s != ApiBaseUrl).Take(9));

                var settings = new AppSettings
                {
                    ApiBaseUrl = ApiBaseUrl,
                    RecentServers = recentServers,
                    LastUpdated = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                AddActivity("⚠️", $"Failed to save settings: {ex.Message}");
            }
        }

        private async Task CheckApiConnectionAsync()
        {
            try
            {
                StatusText = "Connecting...";
                ConnectionStatusText = "API: Connecting...";
                IsConnected = false;

                var isConnected = await _apiService.CheckConnectionAsync();

                if (isConnected)
                {
                    StatusText = "Connected";
                    ConnectionStatusText = $"API: Connected ({ExtractHostFromUrl(ApiBaseUrl)})";
                    IsConnected = true;
                }
                else
                {
                    StatusText = "Disconnected";
                    ConnectionStatusText = "API: Disconnected";
                    IsConnected = false;
                }
            }
            catch (Exception ex)
            {
                StatusText = "Disconnected";
                ConnectionStatusText = $"API: Disconnected ({ex.Message})";
                IsConnected = false;
            }
        }

        private string ExtractHostFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                return $"{uri.Host}:{uri.Port}";
            }
            catch
            {
                return "Unknown";
            }
        }

        private async Task BrowseFileAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
                Title = "Select Project Archive"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedFilePath = openFileDialog.FileName;
                FilePathText = SelectedFilePath;
                IsUploadEnabled = true;

                if (string.IsNullOrEmpty(VersionText))
                {
                    VersionText = "1.0.0";
                }

                await ValidateVersionAsync();
            }
        }

        private async Task UploadFileAsync()
        {
            if (string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
            {
                await _dialogService.ShowMessageAsync("Please select a valid file first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await PerformUploadAsync(false);
        }

        private async Task PerformUploadAsync(bool overrideExisting)
        {
            try
            {
                IsUploadEnabled = false;
                UploadProgress = 0;
                UploadStatusText = "Preparing upload...";
                IsUploadIndeterminate = true;

                var result = await _apiService.UploadProjectAsync(
                    SelectedFilePath,
                    VersionText,
                    DescriptionText,
                    overrideExisting,
                    progress => UploadProgress = progress);

                await HandleUploadSuccessAsync(result);
            }
            catch (VersionConflictException ex)
            {
                await HandleVersionConflictAsync(ex.ConflictResponse);
            }
            catch (Exception ex)
            {
                await HandleUploadErrorAsync(ex);
            }
            finally
            {
                IsUploadEnabled = true;
                IsUploadIndeterminate = false;
            }
        }

        private async Task HandleUploadSuccessAsync(UploadSuccessResponse result)
        {
            UploadProgress = 100;
            UploadStatusText = "Upload completed successfully!";

            var message = result.WasOverridden
                ? $"Project was successfully overridden: {result.ProjectName}"
                : $"Project uploaded successfully: {result.ProjectName}";

            AddActivity("✅", message);

            // Clear form
            FilePathText = "No file selected...";
            VersionText = "";
            DescriptionText = "";
            SelectedFilePath = null;

            await RefreshAsync();
            await _dialogService.ShowMessageAsync(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async Task HandleVersionConflictAsync(VersionConflictResponse conflict)
        {
            UploadProgress = 0;
            UploadStatusText = "Version conflict detected";

            var result = await _dialogService.ShowVersionConflictAsync(conflict);

            if (result.Confirmed)
            {
                if (result.ShouldOverride)
                {
                    AddActivity("⚠️", $"Overriding existing version: {conflict.ConflictingVersion}");
                    await PerformUploadAsync(true);
                }
                else if (!string.IsNullOrEmpty(result.NewVersion))
                {
                    VersionText = result.NewVersion;
                    AddActivity("🔄", $"Changed version to: {result.NewVersion}");
                    await PerformUploadAsync(false);
                }
            }
            else
            {
                UploadStatusText = "Upload cancelled by user";
                AddActivity("❌", "Upload cancelled due to version conflict");
            }
        }

        private async Task HandleUploadErrorAsync(Exception ex)
        {
            UploadProgress = 0;
            UploadStatusText = $"Upload failed: {ex.Message}";
            AddActivity("❌", $"Upload failed: {ex.Message}");
            await _dialogService.ShowMessageAsync($"Upload failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private async Task ValidateVersionAsync()
        {
            if (string.IsNullOrEmpty(VersionText) || string.IsNullOrEmpty(SelectedFilePath))
            {
                VersionValidationText = "";
                return;
            }

            try
            {
                var projectName = Path.GetFileNameWithoutExtension(SelectedFilePath);
                var exists = await _apiService.CheckVersionExistsAsync(projectName, VersionText);

                if (exists)
                {
                    VersionValidationText = $"⚠️ Version {VersionText} already exists - will ask to override";
                }
                else
                {
                    VersionValidationText = $"✅ Version {VersionText} is available";
                }
            }
            catch
            {
                VersionValidationText = "";
            }
        }

        private async Task RefreshAsync()
        {
            await LoadProjectsAsync();
            await LoadStatisticsAsync();
            AddActivity("🔄", "Refreshed project list");
        }

        private async Task LoadProjectsAsync()
        {
            try
            {
                StatusBarText = "Loading projects...";
                var projects = await _apiService.GetProjectsAsync(GroupByProject);

                Projects.Clear();
                foreach (var project in projects)
                {
                    Projects.Add(project);
                }

                StatusBarText = $"Loaded {Projects.Count} projects";
                OnPropertyChanged(nameof(FilteredProjects));
            }
            catch (Exception ex)
            {
                StatusBarText = $"Error loading projects: {ex.Message}";
                await _dialogService.ShowMessageAsync($"Failed to load projects: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadStatisticsAsync()
        {
            try
            {
                Statistics = await _apiService.GetStatisticsAsync();
            }
            catch (Exception ex)
            {
                AddActivity("⚠️", $"Failed to load statistics: {ex.Message}");
            }
        }

        private async Task LoadRunnerStatusAsync()
        {
            try
            {
                StatusBarText = "Loading runner status...";
                var status = await _apiService.GetRunnerStatusAsync();

                RunnerStatusText = status.IsRunning
                    ? $"Status: Running ({status.ProcessCount} processes)"
                    : "Status: Stopped";
                RunnerPathText = $"Path: {status.RunnerPath}";

                RunningProjects.Clear();
                foreach (var project in status.RunningProjects ?? new RunningProjectInfo[0])
                {
                    RunningProjects.Add(project);
                }

                StatusBarText = $"Runner status loaded - {status.ProcessCount} processes running";
                AddActivity("📊", $"Loaded runner status - {status.ProcessCount} processes running");
            }
            catch (Exception ex)
            {
                StatusBarText = $"Error loading runner status: {ex.Message}";
                RunnerStatusText = "Status: Error";
                RunnerPathText = "Path: Unknown";
                AddActivity("❌", $"Failed to load runner status: {ex.Message}");
            }
        }

        private async Task RunProjectAsync(ProjectInfo project)
        {
            if (project == null) return;

            try
            {
                var result = await _apiService.StartProjectAsync(project.Name);
                AddActivity("🚀", $"Started runner for project: {project.Name} (PID: {result.ProcessId})");
                await _dialogService.ShowMessageAsync(
                    $"Runner started successfully!\n\nProject: {project.Name}\nProcess ID: {result.ProcessId}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("already running"))
                {
                    var result = await _dialogService.ShowMessageAsync(
                        $"Project is already running!\n\n{ex.Message}\n\nWould you like to stop it first?",
                        "Project Already Running", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await StopProjectAsync(project);
                        await Task.Delay(2000);
                        await RunProjectAsync(project);
                    }
                }
                else
                {
                    AddActivity("❌", $"Failed to start runner: {ex.Message}");
                    await _dialogService.ShowMessageAsync($"Failed to start runner: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task StopProjectAsync(ProjectInfo project)
        {
            if (project == null) return;

            try
            {
                await _apiService.StopProjectAsync(project.Name);
                AddActivity("🛑", $"Stopped runner for project: {project.Name}");
                await _dialogService.ShowMessageAsync(
                    $"Stop command sent for project: {project.Name}",
                    "Stop Command Sent",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to stop project: {ex.Message}");
                await _dialogService.ShowMessageAsync($"Failed to stop project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task DeleteProjectAsync(ProjectInfo project)
        {
            if (project == null) return;

            var result = await _dialogService.ShowMessageAsync(
                $"Are you sure you want to delete project '{project.Name}'?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _apiService.DeleteProjectAsync(project.Name, project.Version);
                    AddActivity("🗑️", $"Deleted project: {project.Name}");
                    await RefreshAsync();
                    await _dialogService.ShowMessageAsync("Project deleted successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to delete project: {ex.Message}");
                    await _dialogService.ShowMessageAsync($"Failed to delete project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ViewVersions(ProjectInfo project)
        {
            if (project == null) return;
            _dialogService.ShowVersionsWindow(project);
        }

        private async Task CreateVersionAsync()
        {
            if (SelectedProject == null) return;

            var result = await _dialogService.ShowCreateVersionAsync(SelectedProject);
            if (result.Confirmed)
            {
                await PerformCreateVersionAsync(SelectedProject, result.Version, result.Description, false);
            }
        }

        private async Task PerformCreateVersionAsync(ProjectInfo project, string newVersion, string description, bool overrideExisting)
        {
            try
            {
                var result = await _apiService.CreateVersionAsync(project.Name, newVersion, description, overrideExisting);

                var message = result.WasOverridden
                    ? $"Version {newVersion} was overridden for project: {project.Name}"
                    : $"Created new version {newVersion} for project: {project.Name}";

                AddActivity("📋", message);
                await LoadProjectsAsync();
                await _dialogService.ShowMessageAsync(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (VersionConflictException ex)
            {
                var conflictResult = await _dialogService.ShowVersionConflictAsync(ex.ConflictResponse);

                if (conflictResult.Confirmed)
                {
                    if (conflictResult.ShouldOverride)
                    {
                        AddActivity("⚠️", $"Overriding existing version: {ex.ConflictResponse.ConflictingVersion}");
                        await PerformCreateVersionAsync(project, newVersion, description, true);
                    }
                    else if (!string.IsNullOrEmpty(conflictResult.NewVersion))
                    {
                        AddActivity("🔄", $"Changed version to: {conflictResult.NewVersion}");
                        await PerformCreateVersionAsync(project, conflictResult.NewVersion, description, false);
                    }
                }
                else
                {
                    AddActivity("❌", "Create version cancelled due to version conflict");
                }
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to create version: {ex.Message}");
                await _dialogService.ShowMessageAsync($"Failed to create new version: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolder()
        {
            if (SelectedProject == null) return;

            try
            {

                AddActivity("📁", $"Opened folder for project: {SelectedProject.Name}");
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to open folder: {ex.Message}");
                _dialogService.ShowMessageAsync($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error).Wait();
            }
        }

        private async Task StartRunnerAsync()
        {
            try
            {
                var result = await _apiService.StartRunnerAsync();
                AddActivity("🚀", $"Started runner (PID: {result.ProcessId})");
                await _dialogService.ShowMessageAsync(
                    $"Runner started successfully!\nProcess ID: {result.ProcessId}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadRunnerStatusAsync();
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to start runner: {ex.Message}");
                await _dialogService.ShowMessageAsync($"Failed to start runner: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopAllRunnersAsync()
        {
            var result = await _dialogService.ShowMessageAsync(
                "Are you sure you want to stop all running processes?",
                "Confirm Stop All", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _apiService.StopAllRunnersAsync();
                    AddActivity("🛑", "Stopped all runner processes");
                    await _dialogService.ShowMessageAsync("All runner processes stopped successfully!", "Success",
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadRunnerStatusAsync();
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to stop all runners: {ex.Message}");
                    await _dialogService.ShowMessageAsync($"Failed to stop all runners: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task StopRunningProjectAsync(RunningProjectInfo runningProject)
        {
            if (runningProject == null) return;

            try
            {
                await _apiService.StopProjectAsync(runningProject.ProjectName);
                await _dialogService.ShowMessageAsync(
                    $"Stop command sent for project: {runningProject.ProjectName}",
                    "Stop Command Sent", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadRunnerStatusAsync();
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to stop project: {ex.Message}");
                await _dialogService.ShowMessageAsync($"Failed to stop project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task KillProcessAsync(RunningProjectInfo runningProject)
        {
            if (runningProject == null) return;

            var result = await _dialogService.ShowMessageAsync(
                $"Are you sure you want to forcefully kill process {runningProject.ProcessId}?\n\nThis may cause data loss!",
                "Confirm Kill Process", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _apiService.KillProcessAsync(runningProject.ProcessId);
                    AddActivity("💀", $"Killed process {runningProject.ProcessId} ({runningProject.ProjectName})");
                    await _dialogService.ShowMessageAsync(
                        $"Process {runningProject.ProcessId} killed successfully!",
                        "Process Killed", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadRunnerStatusAsync();
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to kill process: {ex.Message}");
                    await _dialogService.ShowMessageAsync($"Failed to kill process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task TestConnectionAsync()
        {
            try
            {
                await CheckApiConnectionAsync();

                if (IsConnected)
                {
                    await _dialogService.ShowMessageAsync("✅ Connection successful!", "Connection Test",
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                    AddActivity("✅", "Connection test successful");
                }
                else
                {
                    await _dialogService.ShowMessageAsync(
                        "❌ Connection failed. Please check the server URL and ensure the API is running.",
                        "Connection Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AddActivity("❌", "Connection test failed");
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync($"❌ Connection test failed: {ex.Message}", "Connection Test",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                AddActivity("❌", $"Connection test error: {ex.Message}");
            }
        }

        private async Task UpdateServerUrlAsync(string newUrl)
        {
            if (string.IsNullOrWhiteSpace(newUrl))
                return;

            newUrl = CleanServerUrl(newUrl);

            if (ApiBaseUrl != newUrl)
            {
                ApiBaseUrl = newUrl;
                _apiService.BaseUrl = newUrl;

                AddActivity("🔗", $"Changed server to: {newUrl}");

                if (!RecentServers.Contains(newUrl))
                {
                    RecentServers.Insert(0, newUrl);
                }

                SaveSettings();
                await CheckApiConnectionAsync();

                if (IsConnected)
                {
                    await LoadProjectsAsync();
                    await LoadStatisticsAsync();
                }
            }
        }

        private string CleanServerUrl(string url)
        {
            url = url.Trim();

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
            }

            if (!url.EndsWith("/api"))
            {
                url = url.TrimEnd('/') + "/api";
            }

            return url;
        }

        private void ShowSettings()
        {
            var result = _dialogService.ShowServerSettings(ApiBaseUrl, RecentServers.ToList());
            if (result.Confirmed && !string.IsNullOrEmpty(result.SelectedServer))
            {
                UpdateServerUrlAsync(result.SelectedServer).Wait();
            }
        }

        private void ShowQuickConnect(object parameter)
        {
            var quickConnectOptions = new[]
            {
                ("🏠 Localhost (HTTPS)", "https://localhost:7001/api"),
                ("🏠 Localhost (HTTP)", "http://localhost:5000/api"),
                ("🌐 Local Network", "http://192.168.1.100:5000/api"),
                ("☁️ Custom Server...", "custom")
            };

            _dialogService.ShowQuickConnectMenu(parameter, quickConnectOptions, async (url) =>
            {
                if (url == "custom")
                {
                    ShowSettings();
                }
                else
                {
                    await UpdateServerUrlAsync(url);
                }
            });
        }

        private async Task CleanupAsync()
        {
            var result = await _dialogService.ShowMessageAsync(
                "This will delete all projects older than 30 days. Continue?",
                "Confirm Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _apiService.CleanupProjectsAsync(30);
                    AddActivity("🧹", "Performed cleanup of old projects");
                    await RefreshAsync();
                    await _dialogService.ShowMessageAsync("Cleanup completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Cleanup failed: {ex.Message}");
                    await _dialogService.ShowMessageAsync($"Cleanup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private ObservableCollection<ProjectInfo> GetFilteredProjects()
        {
            if (string.IsNullOrEmpty(SearchText))
                return Projects;

            var filtered = Projects.Where(p =>
                p.Name.ToLower().Contains(SearchText.ToLower()) ||
                p.Version.ToLower().Contains(SearchText.ToLower())).ToList();

            return new ObservableCollection<ProjectInfo>(filtered);
        }

        private void AddActivity(string icon, string message)
        {
            RecentActivity.Insert(0, new ActivityLog
            {
                Icon = icon,
                Message = message,
                Timestamp = DateTime.Now
            });

            while (RecentActivity.Count > 50)
            {
                RecentActivity.RemoveAt(RecentActivity.Count - 1);
            }
        }

        partial void OnSelectedProjectChanged(ProjectInfo value)
        {
            IsCreateVersionEnabled = value != null;
            IsOpenFolderEnabled = value != null;
        }

        partial void OnGroupByProjectChanged(bool value)
        {
            LoadProjectsAsync().Wait();
        }

        partial void OnSearchTextChanged(string value)
        {
            OnPropertyChanged(nameof(FilteredProjects));
        }

        partial void OnVersionTextChanged(string value)
        {
            ValidateVersionAsync().Wait();
        }
    }
}