using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProjectExporterMvvm.Models;

namespace ProjectExporterMvvm.ViewModels
{
    public class CreateVersionWindowViewModel : BaseViewModel
    {
        private readonly ProjectInfo _baseProject;
        private string _newVersion;
        private string _description;
        private string _previewText;
        private bool _canCreate;

        public string BaseProjectText { get; }
        public string NewVersion
        {
            get => _newVersion;
            set
            {
                if (SetProperty(ref _newVersion, value))
                {
                    UpdatePreview();
                    OnPropertyChanged(nameof(CanCreate));
                }
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                if (SetProperty(ref _description, value))
                {
                    UpdatePreview();
                }
            }
        }

        public string PreviewText
        {
            get => _previewText;
            set => SetProperty(ref _previewText, value);
        }

        public bool CanCreate
        {
            get => !string.IsNullOrWhiteSpace(NewVersion);
        }

        public ICommand CreateCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<bool> CloseRequested;

        public CreateVersionWindowViewModel(ProjectInfo baseProject)
        {
            _baseProject = baseProject ?? throw new ArgumentNullException(nameof(baseProject));

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

            CreateCommand = new RelayCommand(Create, () => CanCreate);
            CancelCommand = new RelayCommand(Cancel);

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var version = string.IsNullOrWhiteSpace(NewVersion) ? "[Version]" : NewVersion;
            var description = string.IsNullOrWhiteSpace(Description) ? "[No description]" : Description;

            PreviewText = $"Project: {_baseProject.BaseProjectName ?? _baseProject.Name}\n" +
                         $"New Version: {version}\n" +
                         $"Description: {description}";
        }

        private void Create()
        {
            if (string.IsNullOrWhiteSpace(NewVersion))
            {
                return;
            }

            // Basic version validation
            if (!Regex.IsMatch(NewVersion, @"^\d+\.\d+\.\d+$"))
            {
                // In a real MVVM implementation, you'd use a dialog service here
                var result = MessageBox.Show("Version format should be like '1.0.0'. Continue anyway?",
                                           "Version Format", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            CloseRequested?.Invoke(this, true);
        }

        private void Cancel()
        {
            CloseRequested?.Invoke(this, false);
        }
    }

    public class ServerSettingsWindowViewModel : BaseViewModel
    {
        private readonly HttpClient _httpClient;
        private string _serverUrl;
        private string _testResultText;
        private bool _isTestingConnection;
        private ObservableCollection<string> _recentServers;
        private string _selectedRecentServer;

        public string ServerUrl
        {
            get => _serverUrl;
            set => SetProperty(ref _serverUrl, value);
        }

        public string TestResultText
        {
            get => _testResultText;
            set => SetProperty(ref _testResultText, value);
        }

        public bool IsTestingConnection
        {
            get => _isTestingConnection;
            set => SetProperty(ref _isTestingConnection, value);
        }

        public ObservableCollection<string> RecentServers
        {
            get => _recentServers;
            set => SetProperty(ref _recentServers, value);
        }

        public string SelectedRecentServer
        {
            get => _selectedRecentServer;
            set
            {
                if (SetProperty(ref _selectedRecentServer, value) && !string.IsNullOrEmpty(value))
                {
                    ServerUrl = value;
                    TestResultText = "";
                }
            }
        }

        public string SelectedServer { get; private set; }

        public ICommand TestServerCommand { get; }
        public ICommand RemoveServerCommand { get; }
        public ICommand AddPresetCommand { get; }
        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<bool> CloseRequested;

        public ServerSettingsWindowViewModel(string currentServer, System.Collections.Generic.List<string> recentServers)
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);

            ServerUrl = currentServer;
            SelectedServer = currentServer;

            RecentServers = new ObservableCollection<string>();
            LoadRecentServers(recentServers);
            AddCommonPresets();

            TestServerCommand = new AsyncRelayCommand(TestServerAsync, () => !IsTestingConnection);
            RemoveServerCommand = new RelayCommand<string>(RemoveServer);
            AddPresetCommand = new RelayCommand(AddPreset);
            OkCommand = new RelayCommand(Ok);
            CancelCommand = new RelayCommand(Cancel);
        }

        private void LoadRecentServers(System.Collections.Generic.List<string> recentServers)
        {
            if (recentServers != null)
            {
                foreach (var server in recentServers.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    RecentServers.Add(server);
                }
            }
        }

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
            var serverUrl = ServerUrl?.Trim();

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                TestResultText = "❌ Please enter a server URL";
                return;
            }

            IsTestingConnection = true;
            TestResultText = "Testing connection...";

            try
            {
                // Clean URL
                if (!serverUrl.StartsWith("http://") && !serverUrl.StartsWith("https://"))
                {
                    serverUrl = "https://" + serverUrl;
                }

                if (!serverUrl.EndsWith("/api"))
                {
                    serverUrl = serverUrl.TrimEnd('/') + "/api";
                }

                var response = await _httpClient.GetAsync($"{serverUrl}/project/health");

                if (response.IsSuccessStatusCode)
                {
                    TestResultText = "✅ Connection successful!";
                    ServerUrl = serverUrl; // Update with cleaned URL
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
            catch (Exception ex)
            {
                TestResultText = $"❌ Error: {ex.Message}";
            }
            finally
            {
                IsTestingConnection = false;
            }
        }

        private void RemoveServer(string serverToRemove)
        {
            if (!string.IsNullOrEmpty(serverToRemove))
            {
                RecentServers.Remove(serverToRemove);
            }
        }

        private void AddPreset()
        {
            // This would typically open an AddPresetWindow
            // For now, we'll use a simple input dialog
            // In a full MVVM implementation, you'd use a dialog service
        }

        private void Ok()
        {
            var serverUrl = ServerUrl?.Trim();

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                // In MVVM, use dialog service instead of MessageBox
                MessageBox.Show("Please enter a server URL.", "Validation Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Clean URL
            if (!serverUrl.StartsWith("http://") && !serverUrl.StartsWith("https://"))
            {
                serverUrl = "https://" + serverUrl;
            }

            if (!serverUrl.EndsWith("/api"))
            {
                serverUrl = serverUrl.TrimEnd('/') + "/api";
            }

            SelectedServer = serverUrl;
            CloseRequested?.Invoke(this, true);
        }

        private void Cancel()
        {
            CloseRequested?.Invoke(this, false);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class VersionConflictWindowViewModel : BaseViewModel
    {
        private readonly VersionConflictResponse _conflict;
        private bool _overrideSelected = false;
        private bool _useNewVersionSelected = true;
        private string _selectedVersion;
        private string _actionPreviewText;

        public string ConflictMessage => _conflict.Message;
        public ProjectInfo ExistingProject => _conflict.ExistingProject;
        public string[] VersionSuggestions { get; }

        public bool OverrideSelected
        {
            get => _overrideSelected;
            set
            {
                if (SetProperty(ref _overrideSelected, value))
                {
                    if (value) UseNewVersionSelected = false;
                    UpdatePreview();
                }
            }
        }

        public bool UseNewVersionSelected
        {
            get => _useNewVersionSelected;
            set
            {
                if (SetProperty(ref _useNewVersionSelected, value))
                {
                    if (value) OverrideSelected = false;
                    UpdatePreview();
                }
            }
        }

        public string SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (SetProperty(ref _selectedVersion, value))
                {
                    UpdatePreview();
                }
            }
        }

        public string ActionPreviewText
        {
            get => _actionPreviewText;
            set => SetProperty(ref _actionPreviewText, value);
        }

        public bool ShouldOverride { get; private set; }
        public string FinalSelectedVersion { get; private set; }

        public ICommand ProceedCommand { get; }
        public ICommand CancelCommand { get; }

        public event EventHandler<bool> CloseRequested;

        public VersionConflictWindowViewModel(VersionConflictResponse conflict)
        {
            _conflict = conflict ?? throw new ArgumentNullException(nameof(conflict));

            // Load version suggestions
            if (conflict.SuggestedVersions?.Any() == true)
            {
                VersionSuggestions = conflict.SuggestedVersions.ToArray();
                SelectedVersion = VersionSuggestions[0];
            }
            else
            {
                VersionSuggestions = new[] { "1.0.1", "1.1.0", "2.0.0" };
                SelectedVersion = VersionSuggestions[0];
            }

            ProceedCommand = new RelayCommand(Proceed);
            CancelCommand = new RelayCommand(Cancel);

            UpdatePreview();
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

        private void Proceed()
        {
            if (OverrideSelected)
            {
                var result = MessageBox.Show(
                    $"Are you absolutely sure you want to override the existing version?\n\n" +
                    $"This will permanently delete:\n" +
                    $"• Project: {_conflict.ConflictingProjectName}\n" +
                    $"• Version: {_conflict.ConflictingVersion}\n" +
                    $"• Size: {_conflict.ExistingProject?.FormattedSize}\n" +
                    $"• Files: {_conflict.ExistingProject?.FileCount}\n\n" +
                    $"This action cannot be undone!",
                    "Confirm Override",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.Yes)
                {
                    ShouldOverride = true;
                    CloseRequested?.Invoke(this, true);
                }
            }
            else if (UseNewVersionSelected)
            {
                var selectedVersion = SelectedVersion?.Trim();

                if (string.IsNullOrEmpty(selectedVersion))
                {
                    MessageBox.Show("Please enter or select a version number.", "Validation Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (selectedVersion == _conflict.ConflictingVersion)
                {
                    MessageBox.Show("Please choose a different version number than the conflicting one.",
                                  "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                FinalSelectedVersion = selectedVersion;
                ShouldOverride = false;
                CloseRequested?.Invoke(this, true);
            }
        }

        private void Cancel()
        {
            CloseRequested?.Invoke(this, false);
        }
    }
}
