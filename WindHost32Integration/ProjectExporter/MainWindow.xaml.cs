using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProjectExporter.Models;

namespace ProjectExporter
{
    public partial class MainWindow : Window
    {
        private readonly HttpClient _httpClient;
        private string _apiBaseUrl = "https://localhost:50000/api"; // Default URL
        private ObservableCollection<ProjectInfo> _projects;
        private ObservableCollection<ActivityLog> _recentActivity;
        private ObservableCollection<RunningProjectInfo> _runningProjects;
        private string _selectedFilePath;
        private readonly string _settingsFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProjectExporter", "settings.json");

        public MainWindow()
        {
            InitializeComponent();
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _projects = new ObservableCollection<ProjectInfo>();
            _recentActivity = new ObservableCollection<ActivityLog>();
            _runningProjects = new ObservableCollection<RunningProjectInfo>();

            ProjectsDataGrid.ItemsSource = _projects;
            RecentActivityListBox.ItemsSource = _recentActivity;

            ProjectsDataGrid.SelectionChanged += ProjectsDataGrid_SelectionChanged;

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            await CheckApiConnection();
            await LoadProjects();
            await LoadStatistics();
            await LoadRunnerStatus();
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
                        _apiBaseUrl = settings.ApiBaseUrl ?? _apiBaseUrl;

                        // Load recent servers
                        ServerComboBox.Items.Clear();
                        foreach (var server in settings.RecentServers ?? new List<string>())
                        {
                            if (!string.IsNullOrWhiteSpace(server))
                            {
                                ServerComboBox.Items.Add(new ComboBoxItem { Content = server });
                            }
                        }

                        // Set current server
                        ServerComboBox.Text = _apiBaseUrl;
                    }
                }
                else
                {
                    // Set default
                    ServerComboBox.Text = _apiBaseUrl;
                }
            }
            catch (Exception ex)
            {
                AddActivity("⚠️", $"Failed to load settings: {ex.Message}");
                ServerComboBox.Text = _apiBaseUrl;
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is TabControl tabControl && tabControl.SelectedItem is TabItem selectedTab)
            {
                // Start auto-refresh when Runner Status tab is selected
                if (selectedTab.Header.ToString().Contains("Runner Status"))
                {
                    //_runnerStatusTimer?.Start();
                    _ = LoadRunnerStatus(); // Load immediately
                }
                else
                {
                    //_runnerStatusTimer?.Stop();
                }
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settingsDir = Path.GetDirectoryName(_settingsFilePath);
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                var recentServers = new List<string>();

                // Add current server first
                if (!string.IsNullOrWhiteSpace(_apiBaseUrl))
                {
                    recentServers.Add(_apiBaseUrl);
                }

                // Add other servers from combo box
                foreach (ComboBoxItem item in ServerComboBox.Items)
                {
                    var server = item.Content?.ToString();
                    if (!string.IsNullOrWhiteSpace(server) && !recentServers.Contains(server))
                    {
                        recentServers.Add(server);
                    }
                }

                // Keep only last 10 servers
                if (recentServers.Count > 10)
                {
                    recentServers = recentServers.Take(10).ToList();
                }

                var settings = new AppSettings
                {
                    ApiBaseUrl = _apiBaseUrl,
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

        private async void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerComboBox.SelectedItem is ComboBoxItem item)
            {
                await UpdateServerUrl(item.Content?.ToString());
            }
        }

        private async void ServerComboBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await UpdateServerUrl(ServerComboBox.Text);
            }
        }

        private async Task UpdateServerUrl(string newUrl)
        {
            if (string.IsNullOrWhiteSpace(newUrl))
                return;

            // Clean and validate URL
            newUrl = newUrl.Trim();

            // Add protocol if missing
            if (!newUrl.StartsWith("http://") && !newUrl.StartsWith("https://"))
            {
                newUrl = "https://" + newUrl;
            }

            // Add /api if missing
            if (!newUrl.EndsWith("/api"))
            {
                newUrl = newUrl.TrimEnd('/') + "/api";
            }

            if (_apiBaseUrl != newUrl)
            {
                _apiBaseUrl = newUrl;
                ServerComboBox.Text = newUrl;

                AddActivity("🔗", $"Changed server to: {newUrl}");

                // Add to recent servers if not already there
                var exists = false;
                foreach (ComboBoxItem item in ServerComboBox.Items)
                {
                    if (item.Content?.ToString() == newUrl)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    ServerComboBox.Items.Insert(0, new ComboBoxItem { Content = newUrl });
                }

                SaveSettings();
                await CheckApiConnection();

                // Reload data if connected
                if (StatusIndicator.Fill == System.Windows.Media.Brushes.LightGreen)
                {
                    await LoadProjects();
                    await LoadStatistics();
                }
            }
        }

        private async void StopProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProjectInfo project)
            {
                try
                {
                    button.IsEnabled = false;
                    button.Content = "🔄 Stopping...";

                    await StopProject(project.Name);
                    MessageBox.Show($"Stop command sent for project: {project.Name}", "Stop Command Sent",
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to stop project: {ex.Message}");
                    MessageBox.Show($"Failed to stop project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "🛑 Stop";
                }
            }
        }

        private async void RunnerStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRunnerStatus();
        }

        private async void StartRunnerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartRunnerButton.IsEnabled = false;
                StartRunnerButton.Content = "🔄 Starting...";

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/runner/start", null);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<RunnerStartResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    AddActivity("🚀", $"Started runner (PID: {result.ProcessId})");
                    MessageBox.Show($"Runner started successfully!\nProcess ID: {result.ProcessId}",
                                   "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadRunnerStatus();
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
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to start runner: {ex.Message}");
                MessageBox.Show($"Failed to start runner: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StartRunnerButton.IsEnabled = true;
                StartRunnerButton.Content = "▶️ Start Runner";
            }
        }


        private async void StopAllRunnersButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to stop all running processes?",
                                        "Confirm Stop All", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    StopAllRunnersButton.IsEnabled = false;
                    StopAllRunnersButton.Content = "🔄 Stopping...";

                    var response = await _httpClient.PostAsync($"{_apiBaseUrl}/runner/stop", null);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        AddActivity("🛑", "Stopped all runner processes");
                        MessageBox.Show("All runner processes stopped successfully!", "Success",
                                       MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadRunnerStatus();
                    }
                    else
                    {
                        throw new Exception($"Failed to stop processes: {responseContent}");
                    }
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to stop all runners: {ex.Message}");
                    MessageBox.Show($"Failed to stop all runners: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    StopAllRunnersButton.IsEnabled = true;
                    StopAllRunnersButton.Content = "⏹️ Stop All";
                }
            }
        }

        private async void RefreshRunnerStatusButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadRunnerStatus();
        }

        private async void StopRunningProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is RunningProjectInfo runningProject)
            {
                try
                {
                    button.IsEnabled = false;
                    button.Content = "🔄";

                    await StopProject(runningProject.ProjectName);
                    MessageBox.Show($"Stop command sent for project: {runningProject.ProjectName}",
                                   "Stop Command Sent", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadRunnerStatus();
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to stop project: {ex.Message}");
                    MessageBox.Show($"Failed to stop project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "🛑 Stop";
                }
            }
        }

        private async void KillProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is RunningProjectInfo runningProject)
            {
                var result = MessageBox.Show($"Are you sure you want to forcefully kill process {runningProject.ProcessId}?\n\nThis may cause data loss!",
                                           "Confirm Kill Process", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        button.IsEnabled = false;
                        button.Content = "🔄";

                        var response = await _httpClient.PostAsync($"{_apiBaseUrl}/runner/kill-process?processId={runningProject.ProcessId}", null);

                        if (response.IsSuccessStatusCode)
                        {
                            AddActivity("💀", $"Killed process {runningProject.ProcessId} ({runningProject.ProjectName})");
                            MessageBox.Show($"Process {runningProject.ProcessId} killed successfully!",
                                           "Process Killed", MessageBoxButton.OK, MessageBoxImage.Information);
                            await LoadRunnerStatus();
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            throw new Exception($"Failed to kill process: {errorContent}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AddActivity("❌", $"Failed to kill process: {ex.Message}");
                        MessageBox.Show($"Failed to kill process: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        button.IsEnabled = true;
                        button.Content = "💀 Kill";
                    }
                }
            }
        }

        private async Task LoadRunnerStatus()
        {
            try
            {
                StatusBarText.Text = "Loading runner status...";

                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/runner/status");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var status = JsonSerializer.Deserialize<RunnerStatusResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Update runner status display
                    RunnerStatusText.Text = status.IsRunning
                        ? $"Status: Running ({status.ProcessCount} processes)"
                        : "Status: Stopped";
                    RunnerPathText.Text = $"Path: {status.RunnerPath}";

                    // Update running projects
                    _runningProjects.Clear();
                    foreach (var project in status.RunningProjects ?? new RunningProjectInfo[0])
                    {
                        _runningProjects.Add(project);
                    }

                    StatusBarText.Text = $"Runner status loaded - {status.ProcessCount} processes running";
                    AddActivity("📊", $"Loaded runner status - {status.ProcessCount} processes running");
                }
                else
                {
                    throw new Exception($"Failed to load runner status: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                StatusBarText.Text = $"Error loading runner status: {ex.Message}";
                RunnerStatusText.Text = "Status: Error";
                RunnerPathText.Text = "Path: Unknown";
                AddActivity("❌", $"Failed to load runner status: {ex.Message}");
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionButton.IsEnabled = false;
            TestConnectionButton.Content = "🔄 Testing...";

            try
            {
                await CheckApiConnection();

                if (StatusIndicator.Fill == System.Windows.Media.Brushes.LightGreen)
                {
                    MessageBox.Show("✅ Connection successful!", "Connection Test",
                                   MessageBoxButton.OK, MessageBoxImage.Information);
                    AddActivity("✅", "Connection test successful");
                }
                else
                {
                    MessageBox.Show("❌ Connection failed. Please check the server URL and ensure the API is running.",
                                   "Connection Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AddActivity("❌", "Connection test failed");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Connection test failed: {ex.Message}", "Connection Test",
                               MessageBoxButton.OK, MessageBoxImage.Error);
                AddActivity("❌", $"Connection test error: {ex.Message}");
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
                TestConnectionButton.Content = "🔗 Test";
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new ServerSettingsWindow(_apiBaseUrl, GetRecentServers())
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                _ = UpdateServerUrl(settingsWindow.SelectedServer);
            }
        }

        private List<string> GetRecentServers()
        {
            var servers = new List<string>();
            foreach (ComboBoxItem item in ServerComboBox.Items)
            {
                var server = item.Content?.ToString();
                if (!string.IsNullOrWhiteSpace(server))
                {
                    servers.Add(server);
                }
            }
            return servers;
        }

        private void QuickConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var quickConnectMenu = new ContextMenu();

            var commonServers = new[]
            {
                ("🏠 Localhost (HTTPS)", "https://localhost:7001/api"),
                ("🏠 Localhost (HTTP)", "http://localhost:5000/api"),
                ("🌐 Local Network", "http://192.168.1.100:5000/api"),
                ("☁️ Custom Server...", "custom")
            };

            foreach (var (label, url) in commonServers)
            {
                var menuItem = new MenuItem
                {
                    Header = label,
                    Tag = url
                };

                menuItem.Click += async (s, args) =>
                {
                    if (menuItem.Tag.ToString() == "custom")
                    {
                        SettingsButton_Click(sender, e);
                    }
                    else
                    {
                        await UpdateServerUrl(menuItem.Tag.ToString());
                    }
                };

                quickConnectMenu.Items.Add(menuItem);
            }

            quickConnectMenu.PlacementTarget = sender as Button;
            quickConnectMenu.IsOpen = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            //_runnerStatusTimer?.Stop();
            //_runnerStatusTimer = null;
            _httpClient?.Dispose();
            SaveSettings();
            base.OnClosed(e);
        }

        private async Task CheckApiConnection()
        {
            try
            {
                StatusText.Text = "Connecting...";
                StatusIndicator.Fill = System.Windows.Media.Brushes.Orange;
                ConnectionStatusText.Text = "API: Connecting...";
                ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Orange;

                // Check both project and runner health
                var projectHealthTask = _httpClient.GetAsync($"{_apiBaseUrl}/project/health");
                var runnerHealthTask = _httpClient.GetAsync($"{_apiBaseUrl}/runner/health");

                await Task.WhenAll(projectHealthTask, runnerHealthTask);

                var projectResponse = await projectHealthTask;
                var runnerResponse = await runnerHealthTask;

                if (projectResponse.IsSuccessStatusCode && runnerResponse.IsSuccessStatusCode)
                {
                    StatusIndicator.Fill = System.Windows.Media.Brushes.LightGreen;
                    StatusText.Text = "Connected";
                    ConnectionStatusText.Text = $"API: Connected ({ExtractHostFromUrl(_apiBaseUrl)})";
                    ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (projectResponse.IsSuccessStatusCode)
                {
                    StatusIndicator.Fill = System.Windows.Media.Brushes.Yellow;
                    StatusText.Text = "Partial";
                    ConnectionStatusText.Text = $"API: Projects OK, Runner unavailable";
                    ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else
                {
                    throw new Exception($"Project API returned status: {projectResponse.StatusCode}");
                }
            }
            catch (TaskCanceledException)
            {
                SetDisconnectedStatus("Connection timeout");
            }
            catch (HttpRequestException ex)
            {
                SetDisconnectedStatus($"Connection error: {ex.Message}");
            }
            catch (Exception ex)
            {
                SetDisconnectedStatus($"Error: {ex.Message}");
            }
        }

        private void SetDisconnectedStatus(string reason)
        {
            StatusIndicator.Fill = System.Windows.Media.Brushes.Red;
            StatusText.Text = "Disconnected";
            ConnectionStatusText.Text = $"API: Disconnected ({reason})";
            ConnectionStatusText.Foreground = System.Windows.Media.Brushes.Red;
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

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "ZIP files (*.zip)|*.zip|All files (*.*)|*.*",
                Title = "Select Project Archive"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _selectedFilePath = openFileDialog.FileName;
                FilePathTextBox.Text = _selectedFilePath;
                FilePathTextBox.Foreground = System.Windows.Media.Brushes.Black;
                UploadButton.IsEnabled = true;

                // Auto-fill version based on filename
                var fileName = Path.GetFileNameWithoutExtension(_selectedFilePath);
                if (string.IsNullOrEmpty(VersionTextBox.Text))
                {
                    VersionTextBox.Text = "1.0.0";
                }

                // Trigger version validation
                await ValidateVersionInput();
            }
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath))
            {
                MessageBox.Show("Please select a valid file first.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await PerformUpload(false); // Start with no override
        }

        private async Task PerformUpload(bool overrideExisting)
        {
            try
            {
                UploadButton.IsEnabled = false;
                UploadProgressBar.Value = 0;
                UploadStatusText.Text = "Preparing upload...";

                using var content = new MultipartFormDataContent();
                using var fileStream = new FileStream(_selectedFilePath, FileMode.Open, FileAccess.Read);
                using var streamContent = new StreamContent(fileStream);

                content.Add(streamContent, "formFile", Path.GetFileName(_selectedFilePath));

                var version = string.IsNullOrWhiteSpace(VersionTextBox.Text) ? null : VersionTextBox.Text;
                var description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? null : DescriptionTextBox.Text;

                var url = $"{_apiBaseUrl}/project/export";
                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(version))
                    queryParams.Add($"version={Uri.EscapeDataString(version)}");
                if (!string.IsNullOrEmpty(description))
                    queryParams.Add($"description={Uri.EscapeDataString(description)}");
                if (overrideExisting)
                    queryParams.Add("overrideExisting=true");

                if (queryParams.Any())
                    url += "?" + string.Join("&", queryParams);

                UploadStatusText.Text = "Uploading...";
                UploadProgressBar.IsIndeterminate = true;

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    await HandleUploadSuccess(responseContent);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    await HandleVersionConflict(responseContent);
                }
                else
                {
                    throw new Exception($"Upload failed ({response.StatusCode}): {responseContent}");
                }
            }
            catch (Exception ex)
            {
                await HandleUploadError(ex);
            }
            finally
            {
                UploadButton.IsEnabled = true;
                UploadProgressBar.IsIndeterminate = false;
            }
        }

        private async Task HandleUploadSuccess(string responseContent)
        {
            try
            {
                var result = JsonSerializer.Deserialize<UploadSuccessResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                UploadProgressBar.Value = 100;
                UploadStatusText.Text = "Upload completed successfully!";

                var message = result.WasOverridden
                    ? $"Project was successfully overridden: {result.ProjectName}"
                    : $"Project uploaded successfully: {result.ProjectName}";

                AddActivity("✅", message);

                // Clear form
                FilePathTextBox.Text = "No file selected...";
                FilePathTextBox.Foreground = System.Windows.Media.Brushes.Gray;
                VersionTextBox.Clear();
                DescriptionTextBox.Clear();
                _selectedFilePath = null;

                // Refresh projects list
                await LoadProjects();
                await LoadStatistics();

                MessageBox.Show(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UploadStatusText.Text = $"Upload succeeded but failed to parse response: {ex.Message}";
            }
        }

        private async Task HandleVersionConflict(string responseContent)
        {
            try
            {
                var conflict = JsonSerializer.Deserialize<VersionConflictResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                UploadProgressBar.Value = 0;
                UploadStatusText.Text = "Version conflict detected";

                // Show version conflict dialog
                var conflictWindow = new VersionConflictWindow(conflict)
                {
                    Owner = this
                };

                var result = conflictWindow.ShowDialog();

                if (result == true)
                {
                    if (conflictWindow.ShouldOverride)
                    {
                        // User chose to override
                        AddActivity("⚠️", $"Overriding existing version: {conflict.ConflictingVersion}");
                        await PerformUpload(true);
                    }
                    else if (!string.IsNullOrEmpty(conflictWindow.SelectedVersion))
                    {
                        // User chose a different version
                        VersionTextBox.Text = conflictWindow.SelectedVersion;
                        AddActivity("🔄", $"Changed version to: {conflictWindow.SelectedVersion}");
                        await PerformUpload(false);
                    }
                }
                else
                {
                    UploadStatusText.Text = "Upload cancelled by user";
                    AddActivity("❌", "Upload cancelled due to version conflict");
                }
            }
            catch (Exception ex)
            {
                UploadStatusText.Text = $"Failed to handle version conflict: {ex.Message}";
                AddActivity("❌", $"Version conflict handling error: {ex.Message}");
            }
        }

        private async Task HandleUploadError(Exception ex)
        {
            UploadProgressBar.Value = 0;
            UploadStatusText.Text = $"Upload failed: {ex.Message}";
            AddActivity("❌", $"Upload failed: {ex.Message}");
            MessageBox.Show($"Upload failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private async void VersionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            await ValidateVersionInput();
        }

        private async Task ValidateVersionInput()
        {
            try
            {
                var version = VersionTextBox.Text?.Trim();
                var fileName = FilePathTextBox.Text;

                if (string.IsNullOrEmpty(version) || fileName == "No file selected..." || string.IsNullOrEmpty(_selectedFilePath))
                {
                    VersionValidationText.Text = "";
                    return;
                }

                var projectName = Path.GetFileNameWithoutExtension(_selectedFilePath);

                // Basic version format validation
                if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
                {
                    VersionValidationText.Text = "⚠️ Version format should be like '1.0.0'";
                    VersionValidationText.Foreground = System.Windows.Media.Brushes.Orange;
                    return;
                }

                // Check if version exists on server
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/project/check-version?projectName={Uri.EscapeDataString(projectName)}&version={Uri.EscapeDataString(version)}");

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<VersionCheckResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (result.Exists)
                    {
                        VersionValidationText.Text = $"⚠️ Version {version} already exists - will ask to override";
                        VersionValidationText.Foreground = System.Windows.Media.Brushes.Orange;
                    }
                    else
                    {
                        VersionValidationText.Text = $"✅ Version {version} is available";
                        VersionValidationText.Foreground = System.Windows.Media.Brushes.Green;
                    }
                }
                else
                {
                    VersionValidationText.Text = "⚠️ Cannot validate version - server unavailable";
                    VersionValidationText.Foreground = System.Windows.Media.Brushes.Gray;
                }
            }
            catch
            {
                // Silently fail version validation - don't interrupt user experience
                VersionValidationText.Text = "";
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadProjects();
            await LoadStatistics();
            AddActivity("🔄", "Refreshed project list");
        }

        private async Task LoadProjects()
        {
            try
            {
                StatusBarText.Text = "Loading projects...";

                var groupByProject = GroupByProjectCheckBox.IsChecked == true;
                var url = $"{_apiBaseUrl}/project/exported?groupByProject={groupByProject}";

                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<ProjectResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _projects.Clear();
                    foreach (var project in result.Projects)
                    {
                        _projects.Add(project);
                    }

                    StatusBarText.Text = $"Loaded {_projects.Count} projects";
                }
                else
                {
                    throw new Exception($"Failed to load projects: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                StatusBarText.Text = $"Error loading projects: {ex.Message}";
                MessageBox.Show($"Failed to load projects: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadStatistics()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/project/stats");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var stats = JsonSerializer.Deserialize<ProjectStatistics>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    TotalProjectsText.Text = stats.TotalProjects.ToString();
                    TotalSizeText.Text = stats.FormattedTotalSize;
                    TotalFilesText.Text = stats.TotalFiles.ToString();
                    UniqueProjectsText.Text = stats.UniqueProjects.ToString();
                }
            }
            catch (Exception ex)
            {
                AddActivity("⚠️", $"Failed to load statistics: {ex.Message}");
            }
        }

        private void ProjectsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectsDataGrid.SelectedItem is ProjectInfo project)
            {
                ProjectNameDetail.Text = $"Name: {project.Name}";
                ProjectVersionDetail.Text = $"Version: {project.Version}";
                ProjectSizeDetail.Text = $"Size: {project.FormattedSize}";
                ProjectFilesDetail.Text = $"Files: {project.FileCount}";
                ProjectCreatedDetail.Text = $"Created: {project.CreatedDate:yyyy-MM-dd HH:mm}";
                ProjectModifiedDetail.Text = $"Modified: {project.LastModifiedDate:yyyy-MM-dd HH:mm}";
                ProjectAccessedDetail.Text = $"Accessed: {project.LastAccessedDate:yyyy-MM-dd HH:mm}";

                CreateVersionButton.IsEnabled = true;
                OpenFolderButton.IsEnabled = true;
            }
            else
            {
                CreateVersionButton.IsEnabled = false;
                OpenFolderButton.IsEnabled = false;
            }
        }

        private async void RunProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProjectInfo project)
            {
                try
                {
                    button.IsEnabled = false;
                    button.Content = "🔄 Starting...";

                    var response = await _httpClient.PostAsync($"{_apiBaseUrl}/runner/start-project?projectName={Uri.EscapeDataString(project.Name)}", null);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var result = JsonSerializer.Deserialize<RunnerStartResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        AddActivity("🚀", $"Started runner for project: {project.Name} (PID: {result.ProcessId})");
                        MessageBox.Show($"Runner started successfully!\n\nProject: {project.Name}\nProcess ID: {result.ProcessId}",
                                       "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        var error = JsonSerializer.Deserialize<RunnerErrorResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (error?.Error?.Contains("already running") == true)
                        {
                            var result = MessageBox.Show($"Project is already running!\n\n{error.Error}\n\nWould you like to stop it first?",
                                                        "Project Already Running", MessageBoxButton.YesNo, MessageBoxImage.Question);

                            if (result == MessageBoxResult.Yes)
                            {
                                await StopProject(project.Name);
                                // Wait a moment then try starting again
                                await Task.Delay(2000);
                                await StartProject(project);
                            }
                        }
                        else
                        {
                            throw new Exception(error?.Error ?? "Unknown error occurred");
                        }
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"HTTP {response.StatusCode}: {errorContent}");
                    }
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to start runner: {ex.Message}");
                    MessageBox.Show($"Failed to start runner: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "📝 Run";
                }
            }
        }

        private async Task StartProject(ProjectInfo project)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/runner/start-project?projectName={Uri.EscapeDataString(project.Name)}", null);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<RunnerStartResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    AddActivity("🚀", $"Started runner for project: {project.Name} (PID: {result.ProcessId})");
                    MessageBox.Show($"Runner started successfully!\n\nProject: {project.Name}\nProcess ID: {result.ProcessId}",
                                   "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    throw new Exception($"Failed to start project: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to start project: {ex.Message}");
                MessageBox.Show($"Failed to start project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task StopProject(string projectName)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/runner/stop?projectName={Uri.EscapeDataString(projectName)}", null);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    AddActivity("🛑", $"Stopped runner for project: {projectName}");
                }
                else
                {
                    throw new Exception($"Failed to stop project: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to stop project: {ex.Message}");
            }
        }

        private async void DeleteProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProjectInfo project)
            {
                var result = MessageBox.Show($"Are you sure you want to delete project '{project.Name}'?",
                                           "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var response = await _httpClient.DeleteAsync($"{_apiBaseUrl}/project/exported/{Uri.EscapeDataString(project.Name)}?version={project.Version}");

                        if (response.IsSuccessStatusCode)
                        {
                            AddActivity("🗑️", $"Deleted project: {project.Name}");
                            await LoadProjects();
                            await LoadStatistics();
                            MessageBox.Show("Project deleted successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            throw new Exception(errorContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        AddActivity("❌", $"Failed to delete project: {ex.Message}");
                        MessageBox.Show($"Failed to delete project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void CleanupButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("This will delete all projects older than 30 days. Continue?",
                                       "Confirm Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var response = await _httpClient.PostAsync($"{_apiBaseUrl}/project/cleanup?daysOld=30", null);
                    var responseContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        AddActivity("🧹", "Performed cleanup of old projects");
                        await LoadProjects();
                        await LoadStatistics();
                        MessageBox.Show("Cleanup completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        throw new Exception(responseContent);
                    }
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Cleanup failed: {ex.Message}");
                    MessageBox.Show($"Cleanup failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GroupByProjectCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            _ = LoadProjects();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Implement search filtering here
            var searchText = SearchTextBox.Text?.ToLower() ?? string.Empty;

            if (string.IsNullOrEmpty(searchText))
            {
                ProjectsDataGrid.ItemsSource = _projects;
            }
            else
            {
                var filtered = _projects.Where(p =>
                    p.Name.ToLower().Contains(searchText) ||
                    p.Version.ToLower().Contains(searchText)).ToList();
                ProjectsDataGrid.ItemsSource = filtered;
            }
        }

        private void ViewVersionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProjectInfo project)
            {
                var versionsWindow = new VersionsWindow(project, _httpClient, _apiBaseUrl);
                versionsWindow.Owner = this;
                versionsWindow.ShowDialog();
            }
        }

        private async void CreateVersionButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectsDataGrid.SelectedItem is ProjectInfo project)
            {
                var createVersionWindow = new CreateVersionWindow(project);
                createVersionWindow.Owner = this;

                if (createVersionWindow.ShowDialog() == true)
                {
                    await PerformCreateVersion(project, createVersionWindow.NewVersion, createVersionWindow.Description, false);
                }
            }
        }

        private async Task PerformCreateVersion(ProjectInfo project, string newVersion, string description, bool overrideExisting)
        {
            try
            {
                var request = new
                {
                    Version = newVersion,
                    Description = description,
                    OverrideExisting = overrideExisting
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/project/exported/{Uri.EscapeDataString(project.Name)}/new-version", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<UploadSuccessResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var message = result.WasOverridden
                        ? $"Version {newVersion} was overridden for project: {project.Name}"
                        : $"Created new version {newVersion} for project: {project.Name}";

                    AddActivity("📋", message);
                    await LoadProjects();
                    MessageBox.Show(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    // Handle version conflict
                    var conflict = JsonSerializer.Deserialize<VersionConflictResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    var conflictWindow = new VersionConflictWindow(conflict)
                    {
                        Owner = this
                    };

                    var result = conflictWindow.ShowDialog();

                    if (result == true)
                    {
                        if (conflictWindow.ShouldOverride)
                        {
                            AddActivity("⚠️", $"Overriding existing version: {conflict.ConflictingVersion}");
                            await PerformCreateVersion(project, newVersion, description, true);
                        }
                        else if (!string.IsNullOrEmpty(conflictWindow.SelectedVersion))
                        {
                            AddActivity("🔄", $"Changed version to: {conflictWindow.SelectedVersion}");
                            await PerformCreateVersion(project, conflictWindow.SelectedVersion, description, false);
                        }
                    }
                    else
                    {
                        AddActivity("❌", "Create version cancelled due to version conflict");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to create version ({response.StatusCode}): {errorContent}");
                }
            }
            catch (Exception ex)
            {
                AddActivity("❌", $"Failed to create version: {ex.Message}");
                MessageBox.Show($"Failed to create new version: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProjectsDataGrid.SelectedItem is ProjectInfo project)
            {
                try
                {
                    Process.Start("explorer.exe", project.FullPath);
                    AddActivity("📁", $"Opened folder for project: {project.Name}");
                }
                catch (Exception ex)
                {
                    AddActivity("❌", $"Failed to open folder: {ex.Message}");
                    MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AddActivity(string icon, string message)
        {
            _recentActivity.Insert(0, new ActivityLog
            {
                Icon = icon,
                Message = message,
                Timestamp = DateTime.Now
            });

            // Keep only last 50 activities
            while (_recentActivity.Count > 50)
            {
                _recentActivity.RemoveAt(_recentActivity.Count - 1);
            }
        }
    }
}