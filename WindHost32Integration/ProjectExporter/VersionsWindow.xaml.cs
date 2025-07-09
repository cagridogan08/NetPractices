using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ProjectExporter.Models;

namespace ProjectExporter
{
    public partial class VersionsWindow : Window
    {
        private readonly ProjectInfo _project;
        private readonly HttpClient _httpClient;
        private readonly string _apiBaseUrl;
        private ObservableCollection<ProjectInfo> _versions;

        public VersionsWindow(ProjectInfo project, HttpClient httpClient, string apiBaseUrl)
        {
            InitializeComponent();
            _project = project;
            _httpClient = httpClient;
            _apiBaseUrl = apiBaseUrl;
            _versions = new ObservableCollection<ProjectInfo>();

            VersionsDataGrid.ItemsSource = _versions;

            ProjectNameText.Text = $"📋 {project.BaseProjectName ?? project.Name}";
            ProjectInfoText.Text = $"All versions • Current: {project.Version}";

            Loaded += VersionsWindow_Loaded;
        }

        private async void VersionsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadVersions();
        }

        private async Task LoadVersions()
        {
            try
            {
                var projectName = _project.BaseProjectName ?? _project.Name;
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/project/exported/{Uri.EscapeDataString(projectName)}/versions");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<VersionsResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    _versions.Clear();
                    foreach (var version in result.Versions)
                    {
                        _versions.Add(version);
                    }

                    ProjectInfoText.Text = $"All versions • Total: {_versions.Count} • Latest: {result.LatestVersion}";
                }
                else
                {
                    throw new Exception($"Failed to load versions: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load versions: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RunVersionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProjectInfo version)
            {
                try
                {
                    var response = await _httpClient.PostAsync($"{_apiBaseUrl}/runner/start?projectFolderName={Uri.EscapeDataString(version.Name)}", null);

                    if (response.IsSuccessStatusCode)
                    {
                        MessageBox.Show($"Runner started for version: {version.Version}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        throw new Exception(errorContent);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to start runner: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteVersionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProjectInfo version)
            {
                var result = MessageBox.Show($"Are you sure you want to delete version '{version.Version}'?",
                                           "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        var response = await _httpClient.DeleteAsync($"{_apiBaseUrl}/project/exported/{Uri.EscapeDataString(version.Name)}");

                        if (response.IsSuccessStatusCode)
                        {
                            await LoadVersions();
                            MessageBox.Show("Version deleted successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            throw new Exception(errorContent);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete version: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadVersions();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
