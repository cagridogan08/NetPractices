// DialogService.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProjectExporter.Services;
using ProjectExporter.ViewModels;
using ProjectExporterMvvm;
using ProjectExporterMvvm.Models;
using ProjectExporterMvvm.Views;
using Task = System.Threading.Tasks.Task;

namespace ProjectExporter.Services
{
    public class DialogService : IDialogService
    {
        private readonly Func<MainWindow> _getMainWindow;

        public DialogService(Func<MainWindow> getMainWindow)
        {
            _getMainWindow = getMainWindow;
        }

        public Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton button, MessageBoxImage icon)
        {
            return Task.FromResult(MessageBox.Show(message, title, button, icon));
        }

        public async Task<(bool Confirmed, bool ShouldOverride, string NewVersion)> ShowVersionConflictAsync(VersionConflictResponse conflict)
        {
            var viewModel = new VersionConflictViewModel(conflict);
            var window = new VersionConflictWindow
            {
                DataContext = viewModel,
                Owner = _getMainWindow()
            };

            var result = window.ShowDialog() ?? false;
            return await Task.FromResult((result, viewModel.ShouldOverride, viewModel.SelectedVersion));
        }

        public async Task<(bool Confirmed, string Version, string Description)> ShowCreateVersionAsync(ProjectInfo project)
        {
            var viewModel = new CreateVersionViewModel(project);
            var window = new CreateVersionWindow
            {
                DataContext = viewModel,
                Owner = _getMainWindow()
            };

            var result = window.ShowDialog() ?? false;
            return await Task.FromResult((result, viewModel.NewVersion, viewModel.Description));
        }

        public (bool Confirmed, string SelectedServer) ShowServerSettings(string currentServer, List<string> recentServers)
        {
            var viewModel = new ServerSettingsViewModel(currentServer, recentServers);
            var window = new ServerSettingsWindow
            {
                DataContext = viewModel,
                Owner = _getMainWindow()
            };

            var result = window.ShowDialog() ?? false;
            return (result, viewModel.SelectedServer);
        }

        public void ShowVersionsWindow(ProjectInfo project)
        {
            var apiService = new ApiService(); // You might want to inject this
            var viewModel = new VersionsViewModel(project, apiService);
            var window = new VersionsWindow
            {
                DataContext = viewModel,
                Owner = _getMainWindow()
            };

            window.Show();
        }

        public void ShowQuickConnectMenu(object placementTarget, (string Label, string Url)[] options, Action<string> onSelected)
        {
            var menu = new ContextMenu();

            foreach (var (label, url) in options)
            {
                var menuItem = new MenuItem
                {
                    Header = label,
                    Tag = url
                };

                menuItem.Click += (s, e) => onSelected(url);
                menu.Items.Add(menuItem);
            }

            menu.PlacementTarget = placementTarget as UIElement;
            menu.IsOpen = true;
        }
    }
}

// CreateVersionViewModel.cs


namespace ProjectExporter.ViewModels
{
    public partial class CreateVersionViewModel : ObservableObject
    {
        private readonly ProjectInfo _baseProject;

        [ObservableProperty]
        private string _newVersion;

        [ObservableProperty]
        private string _description;

        [ObservableProperty]
        private string _baseProjectText;

        [ObservableProperty]
        private string _previewText;

        [ObservableProperty]
        private bool _canCreate;

        public CreateVersionViewModel(ProjectInfo baseProject)
        {
            _baseProject = baseProject;
            BaseProjectText = $"Base project: {baseProject.Name} (v{baseProject.Version})";

            // Auto-suggest next version
            if (Version.TryParse(baseProject.Version, out var currentVersion))
            {
                var nextVersion = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build + 1);
                NewVersion = nextVersion.ToString();
            }
            else
            {
                NewVersion = "1.0.1";
            }

            UpdatePreview();
        }

        partial void OnNewVersionChanged(string value) => UpdatePreview();
        partial void OnDescriptionChanged(string value) => UpdatePreview();

        private void UpdatePreview()
        {
            var version = string.IsNullOrWhiteSpace(NewVersion) ? "[Version]" : NewVersion;
            var description = string.IsNullOrWhiteSpace(Description) ? "[No description]" : Description;

            PreviewText = $"Project: {_baseProject.BaseProjectName ?? _baseProject.Name}\n" +
                         $"New Version: {version}\n" +
                         $"Description: {description}";

            CanCreate = !string.IsNullOrWhiteSpace(NewVersion);
        }
    }
}



namespace ProjectExporter.ViewModels
{
    public partial class VersionConflictViewModel : ObservableObject
    {
        private readonly VersionConflictResponse _conflict;

        [ObservableProperty]
        private bool _overrideSelected;

        [ObservableProperty]
        private bool _useNewVersionSelected = true;

        [ObservableProperty]
        private string _selectedVersion;

        [ObservableProperty]
        private string _conflictMessage;

        [ObservableProperty]
        private string _existingProjectInfo;

        [ObservableProperty]
        private string _actionPreviewText;

        [ObservableProperty]
        private ObservableCollection<string> _versionSuggestions = new();

        public bool ShouldOverride => OverrideSelected;

        public VersionConflictViewModel(VersionConflictResponse conflict)
        {
            _conflict = conflict;
            ConflictMessage = conflict.Message;

            LoadConflictInfo();
            LoadVersionSuggestions();
            UpdatePreview();
        }

        partial void OnOverrideSelectedChanged(bool value)
        {
            if (value) UseNewVersionSelected = false;
            UpdatePreview();
        }

        partial void OnUseNewVersionSelectedChanged(bool value)
        {
            if (value) OverrideSelected = false;
            UpdatePreview();
        }

        partial void OnSelectedVersionChanged(string value) => UpdatePreview();

        private void LoadConflictInfo()
        {
            if (_conflict.ExistingProject != null)
            {
                var existing = _conflict.ExistingProject;
                ExistingProjectInfo = $"Name: {existing.Name}\n" +
                                    $"Version: {existing.Version}\n" +
                                    $"Size: {existing.FormattedSize}\n" +
                                    $"Created: {existing.CreatedDate:yyyy-MM-dd HH:mm}\n" +
                                    $"Modified: {existing.LastModifiedDate:yyyy-MM-dd HH:mm}\n" +
                                    $"Created By: {existing.Metadata?.CreatedBy ?? "Unknown"}";
            }
        }

        private void LoadVersionSuggestions()
        {
            VersionSuggestions.Clear();

            if (_conflict.SuggestedVersions?.Any() == true)
            {
                foreach (var version in _conflict.SuggestedVersions)
                {
                    VersionSuggestions.Add(version);
                }
                SelectedVersion = VersionSuggestions.First();
            }
            else
            {
                VersionSuggestions.Add("1.0.1");
                VersionSuggestions.Add("1.1.0");
                VersionSuggestions.Add("2.0.0");
                SelectedVersion = VersionSuggestions.First();
            }
        }

        private void UpdatePreview()
        {
            if (OverrideSelected)
            {
                ActionPreviewText =
                    $"⚠️ OVERRIDE ACTION:\n\n" +
                    $"• The existing project '{_conflict.ConflictingProjectName}' version '{_conflict.ConflictingVersion}' will be DELETED\n" +
                    $"• Your new upload will replace it completely\n" +
                    $"• This action cannot be undone\n\n" +
                    $"Created: {_conflict.ExistingProject?.CreatedDate:yyyy-MM-dd HH:mm}\n" +
                    $"Size: {_conflict.ExistingProject?.FormattedSize}\n" +
                    $"Files: {_conflict.ExistingProject?.FileCount}";
            }
            else if (UseNewVersionSelected)
            {
                var selectedVersion = SelectedVersion ?? "Unknown";
                ActionPreviewText =
                    $"✅ NEW VERSION ACTION:\n\n" +
                    $"• Your project will be uploaded as version '{selectedVersion}'\n" +
                    $"• The existing version '{_conflict.ConflictingVersion}' will remain unchanged\n" +
                    $"• Both versions will be available in the system\n\n" +
                    $"New project name: {_conflict.ConflictingProjectName}_v{selectedVersion}";
            }
        }

        public bool Validate()
        {
            if (UseNewVersionSelected)
            {
                if (string.IsNullOrEmpty(SelectedVersion))
                    return false;

                if (SelectedVersion == _conflict.ConflictingVersion)
                    return false;
            }

            return true;
        }
    }
}



namespace ProjectExporter.ViewModels
{
    public partial class ServerSettingsViewModel : ObservableObject
    {
        private readonly HttpClient _httpClient;

        [ObservableProperty]
        private string _serverUrl;

        [ObservableProperty]
        private string _selectedServer;

        [ObservableProperty]
        private string _testResultText = "";

        [ObservableProperty]
        private bool _isTestingConnection;

        [ObservableProperty]
        private ObservableCollection<string> _recentServers = new();

        public ServerSettingsViewModel(string currentServer, System.Collections.Generic.List<string> recentServers)
        {
            _httpClient = new HttpClient { Timeout = System.TimeSpan.FromSeconds(10) };

            ServerUrl = currentServer;
            SelectedServer = currentServer;

            foreach (var server in recentServers?.Where(s => !string.IsNullOrWhiteSpace(s)) ?? Enumerable.Empty<string>())
            {
                RecentServers.Add(server);
            }

            AddCommonPresets();

            TestServerCommand = new AsyncRelayCommand(TestServerAsync);
            RemoveServerCommand = new RelayCommand<string>(RemoveServer);
            SelectServerCommand = new RelayCommand<string>(SelectServer);
        }

        public IAsyncRelayCommand TestServerCommand { get; }
        public IRelayCommand<string> RemoveServerCommand { get; }
        public IRelayCommand<string> SelectServerCommand { get; }

        private void AddCommonPresets()
        {
            var presets = new[]
            {
                "https://localhost:7001/api",
                "http://localhost:5000/api",
                "https://localhost:5001/api",
                "http://192.168.1.100:5000/api",
                "https://api.yourcompany.com/api"
            };

            foreach (var preset in presets)
            {
                if (!RecentServers.Contains(preset))
                {
                    RecentServers.Add(preset);
                }
            }
        }

        private async Task TestServerAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl))
            {
                TestResultText = "❌ Please enter a server URL";
                return;
            }

            IsTestingConnection = true;
            TestResultText = "Testing connection...";

            try
            {
                var cleanedUrl = CleanServerUrl(ServerUrl);
                var response = await _httpClient.GetAsync($"{cleanedUrl}/project/health");

                if (response.IsSuccessStatusCode)
                {
                    TestResultText = "✅ Connection successful!";
                    ServerUrl = cleanedUrl;
                }
                else
                {
                    TestResultText = $"❌ Server returned: {response.StatusCode}";
                }
            }
            catch (TaskCanceledException)
            {
                TestResultText = "❌ Connection timeout";
            }
            catch (HttpRequestException ex)
            {
                TestResultText = $"❌ Connection failed: {ex.Message}";
            }
            catch (System.Exception ex)
            {
                TestResultText = $"❌ Error: {ex.Message}";
            }
            finally
            {
                IsTestingConnection = false;
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

        private void RemoveServer(string server)
        {
            if (!string.IsNullOrEmpty(server))
            {
                RecentServers.Remove(server);
            }
        }

        private void SelectServer(string server)
        {
            if (!string.IsNullOrEmpty(server))
            {
                ServerUrl = server;
                TestResultText = "";
            }
        }

        public bool ValidateAndPrepare()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl))
                return false;

            SelectedServer = CleanServerUrl(ServerUrl);
            return true;
        }
    }
}


namespace ProjectExporter.ViewModels
{
    public partial class VersionsViewModel : ObservableObject
    {
        private readonly ProjectInfo _project;
        private readonly IApiService _apiService;

        [ObservableProperty]
        private ObservableCollection<ProjectInfo> _versions = new();

        [ObservableProperty]
        private string _projectNameText;

        [ObservableProperty]
        private string _projectInfoText;

        public VersionsViewModel(ProjectInfo project, IApiService apiService)
        {
            _project = project;
            _apiService = apiService;

            ProjectNameText = $"📋 {project.BaseProjectName ?? project.Name}";
            ProjectInfoText = $"All versions • Current: {project.Version}";

            RefreshCommand = new AsyncRelayCommand(LoadVersionsAsync);
            RunVersionCommand = new AsyncRelayCommand<ProjectInfo>(RunVersionAsync);
            DeleteVersionCommand = new AsyncRelayCommand<ProjectInfo>(DeleteVersionAsync);

            // Load versions on initialization
            Task.Run(async () => await LoadVersionsAsync());
        }

        public IAsyncRelayCommand RefreshCommand { get; }
        public IAsyncRelayCommand<ProjectInfo> RunVersionCommand { get; }
        public IAsyncRelayCommand<ProjectInfo> DeleteVersionCommand { get; }

        private async Task LoadVersionsAsync()
        {
            try
            {
                var projectName = _project.BaseProjectName ?? _project.Name;
                var result = await _apiService.GetProjectVersionsAsync(projectName);

                Versions.Clear();
                foreach (var version in result.Versions)
                {
                    Versions.Add(version);
                }

                ProjectInfoText = $"All versions • Total: {Versions.Count} • Latest: {result.LatestVersion}";
            }
            catch (Exception ex)
            {
                // Handle error - you might want to inject a dialog service here
                System.Windows.MessageBox.Show($"Failed to load versions: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task RunVersionAsync(ProjectInfo version)
        {
            if (version == null) return;

            try
            {
                await _apiService.StartProjectAsync(version.Name);
                System.Windows.MessageBox.Show($"Runner started for version: {version.Version}", "Success",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to start runner: {ex.Message}", "Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task DeleteVersionAsync(ProjectInfo version)
        {
            if (version == null) return;

            var result = System.Windows.MessageBox.Show($"Are you sure you want to delete version '{version.Version}'?",
                "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    await _apiService.DeleteProjectAsync(version.Name, version.Version);
                    await LoadVersionsAsync();
                    System.Windows.MessageBox.Show("Version deleted successfully!", "Success",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to delete version: {ex.Message}", "Error",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }
    }
}



namespace ProjectExporter.ViewModels
{
    public partial class AddPresetViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _presetUrl = "";

        public string PresetUrlResult { get; private set; }

        public AddPresetViewModel()
        {
        }

        public bool Validate()
        {
            if (string.IsNullOrWhiteSpace(PresetUrl))
                return false;

            PresetUrlResult = PresetUrl.Trim();
            return true;
        }
    }
}