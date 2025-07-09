
using System.Text.Json;
using System.Windows;
using Models.SharedLibrary.Models;

namespace MainApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly BackgroundProcessService _processService;
        private string _currentDataFileId;

        public MainWindow()
        {
            InitializeComponent();
            _processService = new BackgroundProcessService();

            // Initially disable operation buttons
            SetOperationButtonsEnabled(false);
        }

        private async void StartProcess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartProcessBtn.IsEnabled = false;
                ProcessStatusText.Text = "Starting...";
                StatusBarText.Text = "Starting background process...";

                var success = await _processService.StartBackgroundProcessAsync();

                if (success)
                {
                    ProcessStatusText.Text = "Running";
                    StatusBarText.Text = "Background process started successfully";
                    SetOperationButtonsEnabled(true);
                    StopProcessBtn.IsEnabled = true;

                    AddToResults("✅ Background process started successfully");
                }
                else
                {
                    ProcessStatusText.Text = "Failed to Start";
                    StatusBarText.Text = "Failed to start background process";
                    StartProcessBtn.IsEnabled = true;

                    AddToResults("❌ Failed to start background process");
                }
            }
            catch (Exception ex)
            {
                ProcessStatusText.Text = "Error";
                StatusBarText.Text = $"Error: {ex.Message}";
                StartProcessBtn.IsEnabled = true;

                AddToResults($"❌ Error starting process: {ex.Message}");
                MessageBox.Show($"Error starting background process: {ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopProcess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _processService.StopBackgroundProcess();
                ProcessStatusText.Text = "Stopped";
                StatusBarText.Text = "Background process stopped";

                SetOperationButtonsEnabled(false);
                StartProcessBtn.IsEnabled = true;
                StopProcessBtn.IsEnabled = false;

                AddToResults("🛑 Background process stopped");
            }
            catch (Exception ex)
            {
                AddToResults($"❌ Error stopping process: {ex.Message}");
            }
        }

        private async void ImportData_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteOperationAsync("Import Data", async () =>
            {
                var response = await _processService.ProcessDataAsync("import", new Dictionary<string, object>
                {
                    ["source"] = "database",
                    ["format"] = "json"
                });

                if (response.Success && response.Data != null)
                {
                    var resultData = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        response.Data.ToString());

                    _currentDataFileId = resultData["FileId"].ToString();
                    var recordCount = resultData["RecordCount"].ToString();

                    AddToResults($"✅ Import completed: {recordCount} records imported");
                    AddToResults($"📁 Data file ID: {_currentDataFileId}");

                    // Enable export button
                    ExportDataBtn.IsEnabled = true;
                }
                else
                {
                    AddToResults($"❌ Import failed: {response.Message}");
                }
            });
        }

        private async void ExportData_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentDataFileId))
            {
                MessageBox.Show("No data to export. Please import data first.",
                              "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await ExecuteOperationAsync("Export Data", async () =>
            {
                var response = await _processService.ProcessDataAsync("export", new Dictionary<string, object>
                {
                    ["fileId"] = _currentDataFileId,
                    ["format"] = "csv"
                });

                if (response.Success && response.Data != null)
                {
                    var resultData = JsonSerializer.Deserialize<Dictionary<string, object>>(
                        response.Data.ToString());

                    var exportPath = resultData["ExportPath"].ToString();
                    var recordCount = resultData["RecordCount"].ToString();

                    AddToResults($"✅ Export completed: {recordCount} records exported");
                    AddToResults($"📂 Export file: {exportPath}");

                    // Clear current data file ID
                    _currentDataFileId = null;
                    ExportDataBtn.IsEnabled = false;
                }
                else
                {
                    AddToResults($"❌ Export failed: {response.Message}");
                }
            });
        }

        private async void ProcessLarge_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteOperationAsync("Process Large Dataset", async () =>
            {
                // Show progress bar for long operations
                ProgressBar.Visibility = Visibility.Visible;
                ProgressBar.IsIndeterminate = true;

                var response = await _processService.ProcessDataAsync("process_large_dataset",
                    new Dictionary<string, object>
                    {
                        ["size"] = 10000,
                        ["operation"] = "heavy_calculation"
                    });

                ProgressBar.Visibility = Visibility.Collapsed;

                if (response.Success)
                {
                    AddToResults($"✅ Large dataset processing completed");
                    AddToResults($"📊 Progress: {response.Progress}%");
                }
                else
                {
                    AddToResults($"❌ Processing failed: {response.Message}");
                }
            });
        }

        private async void SendFileData_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteOperationAsync("Send File Data", async () =>
            {
                // Create sample large dataset
                var largeDataset = Enumerable.Range(1, 1000).Select(i => new DataRecord
                {
                    Id = i,
                    Name = $"Large Dataset Record {i}",
                    Value = i * 15.75m,
                    CreatedAt = DateTime.Now.AddSeconds(-i)
                }).ToList();

                AddToResults($"📤 Sending large dataset ({largeDataset.Count} records)...");

                // Send large dataset via file transfer
                var fileId = await _processService.SendLargeDatasetAsync(largeDataset);

                AddToResults($"✅ Large dataset sent successfully");
                AddToResults($"📁 File ID: {fileId}");

                // Process the large dataset
                var response = await _processService.ProcessDataAsync("export", new Dictionary<string, object>
                {
                    ["fileId"] = fileId,
                    ["format"] = "csv"
                });

                if (response.Success)
                {
                    AddToResults($"✅ Large dataset processed and exported");
                }
                else
                {
                    AddToResults($"❌ Processing failed: {response.Message}");
                }
            });
        }

        private async Task ExecuteOperationAsync(string operationName, Func<Task> operation)
        {
            try
            {
                SetOperationButtonsEnabled(false);
                StatusText.Text = $"Executing {operationName}...";
                StatusBarText.Text = $"Processing: {operationName}";

                AddToResults($"🔄 Starting: {operationName}");

                await operation();

                StatusText.Text = "Ready";
                StatusBarText.Text = "Ready";
            }
            catch (Exception ex)
            {
                AddToResults($"❌ Error in {operationName}: {ex.Message}");
                MessageBox.Show($"Error in {operationName}: {ex.Message}",
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                StatusText.Text = "Error occurred";
                StatusBarText.Text = $"Error in {operationName}";
            }
            finally
            {
                SetOperationButtonsEnabled(true);
            }
        }

        private void SetOperationButtonsEnabled(bool enabled)
        {
            ImportDataBtn.IsEnabled = enabled;
            ProcessLargeBtn.IsEnabled = enabled;
            SendFileBtn.IsEnabled = enabled;

            // Export button is conditionally enabled based on data availability
            if (enabled && !string.IsNullOrEmpty(_currentDataFileId))
            {
                ExportDataBtn.IsEnabled = true;
            }
            else
            {
                ExportDataBtn.IsEnabled = false;
            }
        }

        private void AddToResults(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var formattedMessage = $"[{timestamp}] {message}";

            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(ResultsTextBox.Text))
                {
                    ResultsTextBox.Text += Environment.NewLine;
                }
                ResultsTextBox.Text += formattedMessage;
                ResultsTextBox.ScrollToEnd();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up background process when window closes
            _processService.StopBackgroundProcess();
            base.OnClosed(e);
        }
    }
}