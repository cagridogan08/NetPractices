using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ProjectExporter
{
    /// <summary>
    /// Interaction logic for ServerSettingsWindow.xaml
    /// </summary>
    public partial class ServerSettingsWindow : Window
    {
        private readonly HttpClient _httpClient;
        private readonly List<string> _recentServers;

        public string SelectedServer { get; private set; }

        public ServerSettingsWindow(string currentServer, List<string> recentServers)
        {
            InitializeComponent();

            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
            _recentServers = recentServers ?? new List<string>();

            ServerUrlTextBox.Text = currentServer;
            SelectedServer = currentServer;

            LoadRecentServers();
            AddCommonPresets();
        }

        private void LoadRecentServers()
        {
            RecentServersListBox.Items.Clear();
            foreach (var server in _recentServers.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                RecentServersListBox.Items.Add(server);
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
                if (!_recentServers.Contains(preset))
                {
                    RecentServersListBox.Items.Add(preset);
                }
            }
        }

        private async void TestServerButton_Click(object sender, RoutedEventArgs e)
        {
            var serverUrl = ServerUrlTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                TestResultText.Text = "❌ Please enter a server URL";
                TestResultText.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            TestServerButton.IsEnabled = false;
            TestServerButton.Content = "🔄 Testing...";
            TestResultText.Text = "Testing connection...";
            TestResultText.Foreground = System.Windows.Media.Brushes.Blue;

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
                    TestResultText.Text = "✅ Connection successful!";
                    TestResultText.Foreground = System.Windows.Media.Brushes.Green;
                    ServerUrlTextBox.Text = serverUrl; // Update with cleaned URL
                }
                else
                {
                    TestResultText.Text = $"❌ Server returned: {response.StatusCode}";
                    TestResultText.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            catch (TaskCanceledException)
            {
                TestResultText.Text = "❌ Connection timeout";
                TestResultText.Foreground = System.Windows.Media.Brushes.Red;
            }
            catch (HttpRequestException ex)
            {
                TestResultText.Text = $"❌ Connection failed: {ex.Message}";
                TestResultText.Foreground = System.Windows.Media.Brushes.Red;
            }
            catch (Exception ex)
            {
                TestResultText.Text = $"❌ Error: {ex.Message}";
                TestResultText.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestServerButton.IsEnabled = true;
                TestServerButton.Content = "🔗 Test Connection";
            }
        }

        private void RecentServersListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RecentServersListBox.SelectedItem is string selectedServer)
            {
                ServerUrlTextBox.Text = selectedServer;
                TestResultText.Text = "";
            }
        }

        private void RemoveServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string serverToRemove)
            {
                RecentServersListBox.Items.Remove(serverToRemove);
                if (_recentServers.Contains(serverToRemove))
                {
                    _recentServers.Remove(serverToRemove);
                }
            }
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var presetWindow = new AddPresetWindow();
            presetWindow.Owner = this;

            if (presetWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(presetWindow.PresetUrl))
            {
                var preset = presetWindow.PresetUrl.Trim();
                if (!RecentServersListBox.Items.Contains(preset))
                {
                    RecentServersListBox.Items.Add(preset);
                }
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var serverUrl = ServerUrlTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                MessageBox.Show("Please enter a server URL.", "Validation Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                ServerUrlTextBox.Focus();
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
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
