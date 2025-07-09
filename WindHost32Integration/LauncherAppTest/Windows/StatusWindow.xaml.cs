using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Messaging.ModelLibrary;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TESA.Desktop.Launcher.Services;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace TESA.Desktop.Launcher.Windows
{
    /// <summary>
    /// StatusWindow.xaml.cs - Status Display Window
    /// </summary>
    public partial class StatusWindow : Window
    {
        private readonly ILogger<StatusWindow> _logger;
        private readonly StatusService _statusService;
        private readonly ProcessLauncherService _processLauncherService;
        private readonly HealthCheckService _healthCheckService;
        private readonly IMessagingService _namedPipeServerService;
        private DispatcherTimer _refreshTimer;
        private StatusWindowViewModel _viewModel;

        public StatusWindow(LauncherStatus initialStatus)
        {
            InitializeComponent();

            // Get services from the current application's service provider
            var app = Application.Current as App;
            var serviceProvider = app?.Services;

            if (serviceProvider != null)
            {
                _logger = serviceProvider.GetRequiredService<ILogger<StatusWindow>>();
                _statusService = serviceProvider.GetRequiredService<StatusService>();
                _processLauncherService = serviceProvider.GetRequiredService<ProcessLauncherService>();
                _healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();
                _namedPipeServerService = serviceProvider.GetRequiredService<IMessagingService>();
            }
            else
            {
                // Fallback if services aren't available
                _logger = new NullLogger<StatusWindow>();
                throw new InvalidOperationException("Service provider not available");
            }

            InitializeWindow(initialStatus);
            SetupRefreshTimer();
        }

        private void InitializeWindow(LauncherStatus initialStatus)
        {
            Title = "TESA Desktop Launcher - Status";
            Width = 800;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Initialize view model
            _viewModel = new StatusWindowViewModel
            {
                Status = initialStatus
            };

            // Load initial data
            LoadStatusData();

            // Set data context
            DataContext = _viewModel;

            _logger.LogInformation("Status window initialized");
        }

        private void SetupRefreshTimer()
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                LoadStatusData();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing status window");
            }
        }

        private void LoadStatusData()
        {
            try
            {
                // Update status
                _viewModel.Status = _statusService.GetCurrentStatus();

                // Update running processes
                _viewModel.RunningProcesses = _processLauncherService.GetRunningProcesses();

                // Update health summary
                _viewModel.HealthSummary = _healthCheckService.GetHealthCheckSummary();

                // Update system info
                _viewModel.SystemInfo = _statusService.GetSystemInfo();

                // Update performance counters
                _viewModel.PerformanceCounters = _statusService.GetPerformanceCounters();

                // Update request statistics
                _viewModel.RequestStatistics = _statusService.GetRequestStatistics();

                // Update pipe server info


                // Update UI
                UpdateStatusDisplay();
                UpdateProcessList();
                UpdateHealthDisplay();
                UpdateSystemInfoDisplay();
                UpdatePerformanceDisplay();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading status data");
            }
        }

        private void UpdateStatusDisplay()
        {
            // Update main status
            StatusTextBlock.Text = _viewModel.Status.IsHealthy ? "✅ Running" : "❌ Error";
            ProcessIdTextBlock.Text = _viewModel.Status.ProcessId.ToString();
            StartTimeTextBlock.Text = _viewModel.Status.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            UptimeTextBlock.Text = _viewModel.Status.GetFormattedUptime();
            RequestsTextBlock.Text = _viewModel.Status.TotalRequestsProcessed.ToString();
            LastRequestTextBlock.Text = _viewModel.Status.GetLastRequestText();
            VersionTextBlock.Text = _viewModel.Status.Version;
            MemoryUsageTextBlock.Text = _viewModel.Status.GetFormattedMemoryUsage();

            // Update request statistics
            RequestsPerHourTextBlock.Text = _viewModel.RequestStatistics.RequestsPerHour.ToString("F2");

            // Update pipe server info
            PipeNameTextBlock.Text = _viewModel.PipeServerInfo.PipeName;
            PipeStatusTextBlock.Text = _viewModel.PipeServerInfo.IsListening ? "✅ Listening" : "❌ Not Listening";
            ActiveClientsTextBlock.Text = _viewModel.PipeServerInfo.ActiveClientCount.ToString();
        }

        private void UpdateProcessList()
        {
            ProcessListBox.Items.Clear();

            foreach (var process in _viewModel.RunningProcesses)
            {
                var duration = DateTime.UtcNow - process.StartTime;
                var item = new ListBoxItem
                {
                    Content = $"PID: {process.ProcessId} - {process.ProjectName} (Running: {duration:hh\\:mm\\:ss})",
                    Tag = process
                };
                ProcessListBox.Items.Add(item);
            }

            if (!_viewModel.RunningProcesses.Any())
            {
                ProcessListBox.Items.Add(new ListBoxItem
                {
                    Content = "No running processes",
                    IsEnabled = false
                });
            }
        }

        private void UpdateHealthDisplay()
        {
            var health = _viewModel.HealthSummary;

            HealthStatusTextBlock.Text = health.IsHealthy ? "✅ Healthy" : "❌ Unhealthy";
            LastHealthCheckTextBlock.Text = health.LastCheckTime == DateTime.MinValue ?
                "Never" : health.LastCheckTime.ToString("HH:mm:ss");
            HealthIssuesTextBlock.Text = health.TotalIssues.ToString();

            if (!string.IsNullOrEmpty(health.LastError))
            {
                HealthErrorTextBlock.Text = health.LastError;
                HealthErrorTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                HealthErrorTextBlock.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSystemInfoDisplay()
        {
            var sysInfo = _viewModel.SystemInfo;

            OSTextBlock.Text = sysInfo.OperatingSystem;
            ProcessorCountTextBlock.Text = sysInfo.ProcessorCount.ToString();
            MachineNameTextBlock.Text = sysInfo.MachineName;
            UserNameTextBlock.Text = $"{sysInfo.UserDomainName}\\{sysInfo.UserName}";
            CLRVersionTextBlock.Text = sysInfo.CLRVersion;
            ArchitectureTextBlock.Text = sysInfo.Is64BitProcess ? "64-bit" : "32-bit";
        }

        private void UpdatePerformanceDisplay()
        {
            var perf = _viewModel.PerformanceCounters;

            WorkingSetTextBlock.Text = perf.WorkingSet64.GetFormattedFileSize();
            VirtualMemoryTextBlock.Text = perf.VirtualMemorySize64.GetFormattedFileSize();
            PrivateMemoryTextBlock.Text = perf.PrivateMemorySize64.GetFormattedFileSize();
            HandleCountTextBlock.Text = perf.HandleCount.ToString();
            ThreadCountTextBlock.Text = perf.ThreadCount.ToString();
            ProcessorTimeTextBlock.Text = perf.TotalProcessorTime.ToString(@"hh\:mm\:ss");
        }

        #region Event Handlers

        private void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestConnectionButton.IsEnabled = false;
            TestConnectionButton.Content = "Testing...";


        }

        private void ViewLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logPath = _statusService.GetLogFilePath();
                if (File.Exists(logPath))
                {
                    Process.Start("notepad.exe", logPath);
                }
                else
                {
                    MessageBox.Show("Log file not found", "View Logs", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open log file");
                MessageBox.Show($"Failed to open log file: {ex.Message}", "View Logs",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenLogDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logPath = _statusService.GetLogFilePath();
                var logDir = Path.GetDirectoryName(logPath);

                if (Directory.Exists(logDir))
                {
                    Process.Start("explorer.exe", logDir);
                }
                else
                {
                    MessageBox.Show("Log directory not found", "Open Log Directory",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open log directory");
                MessageBox.Show($"Failed to open log directory: {ex.Message}", "Open Log Directory",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListBox.SelectedItem is ListBoxItem selectedItem &&
                selectedItem.Tag is LaunchedProcessInfo processInfo)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to stop process {processInfo.ProcessId} ({processInfo.ProjectName})?",
                    "Stop Process",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var success = await _processLauncherService.StopProcessAsync(processInfo.ProcessId);

                            Dispatcher.Invoke(() =>
                            {
                                if (success)
                                {
                                    MessageBox.Show("Process stopped successfully", "Stop Process",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                    LoadStatusData(); // Refresh the list
                                }
                                else
                                {
                                    MessageBox.Show("Failed to stop process", "Stop Process",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show($"Error stopping process: {ex.Message}", "Stop Process",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                    });
                }
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadStatusData();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            //Close();
        }

        #endregion

        protected override void OnClosing(CancelEventArgs e)
        {
            _refreshTimer?.Stop();
            _logger.LogInformation("Status window closing");
            base.OnClosing(e);
        }
    }

    #region Null Logger Implementation

    public class NullLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) => new NullDisposable();
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) { }

        private class NullDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    #endregion
}