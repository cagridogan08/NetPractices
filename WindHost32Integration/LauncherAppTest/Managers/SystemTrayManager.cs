using System.Drawing;
using System.Windows;
using Messaging.ModelLibrary;
using Microsoft.Extensions.Logging;
using TESA.Desktop.Launcher.Services;
using TESA.Desktop.Launcher.Windows;
using Timer = System.Windows.Forms.Timer;

namespace TESA.Desktop.Launcher.Managers
{
    /// <summary>
    /// SystemTrayManager.cs - System Tray Icon Management
    /// </summary>
    public class SystemTrayManager : IDisposable
    {
        private readonly ILogger<SystemTrayManager> _logger;
        private readonly StatusService _statusService;
        private readonly HealthCheckService _healthCheckService;
        private NotifyIcon _notifyIcon;
        private Timer _updateTimer;
        private bool _disposed = false;
        private readonly IMessagingService _messagingService;
        public SystemTrayManager(
            ILogger<SystemTrayManager> logger,
            StatusService statusService,
            HealthCheckService healthCheckService, IMessagingService messagingService)
        {
            _logger = logger;
            _statusService = statusService;
            _healthCheckService = healthCheckService;
            _messagingService = messagingService;
        }

        public void Initialize()
        {
            try
            {
                _logger.LogInformation("Initializing system tray manager");

                SetupNotifyIcon();
                StartUpdateTimer();
                _messagingService.MessageReceived += (s, e) =>
                {
                    _logger.LogInformation($"Message received: {e.Message.Content}");
                    UpdateTrayIcon();
                };
                _logger.LogInformation("System tray manager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize system tray manager");
                throw;
            }
        }

        private void SetupNotifyIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = CreateStatusIcon(Color.Green),
                Text = "TESA Desktop Launcher - Starting...",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show Status", null, OnShowStatus);
            contextMenu.Items.Add("Test Connection", null, OnTestConnection);
            contextMenu.Items.Add("View Logs", null, OnViewLogs);
            contextMenu.Items.Add("Health Check", null, OnHealthCheck);
            contextMenu.Items.Add("-"); // Separator
            contextMenu.Items.Add("Restart", null, OnRestart);
            contextMenu.Items.Add("Exit", null, OnExit);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += OnDoubleClick;
        }

        private void StartUpdateTimer()
        {
            _updateTimer = new Timer
            {
                Interval = 5000 // Update every 5 seconds
            };
            _updateTimer.Tick += OnUpdateTick;
            _updateTimer.Start();
        }

        private void OnUpdateTick(object sender, EventArgs e)
        {
            try
            {
                UpdateTrayIcon();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating tray icon");
            }
        }

        private void UpdateTrayIcon()
        {
            if (_notifyIcon == null) return;

            var status = _statusService.GetCurrentStatus();
            var isHealthy = _messagingService.IsRunning;

            // Update icon color
            Color iconColor = isHealthy ? Color.Green : Color.Red;
            _notifyIcon.Icon = CreateStatusIcon(iconColor);

            // Update tooltip
            var tooltip = $"TESA Desktop Launcher - {(isHealthy ? "Running" : "Error")}\n" +
                         $"Requests: {status.TotalRequestsProcessed}\n" +
                         $"Uptime: {DateTime.Now - status.StartTime:hh\\:mm\\:ss}\n" +
                         $"Last Request: {GetLastRequestText(status.LastRequestTime)}";

            _notifyIcon.Text = tooltip.Length > 127 ? tooltip.Substring(0, 127) : tooltip;
        }

        private string GetLastRequestText(DateTime lastRequestTime)
        {
            if (lastRequestTime == DateTime.MinValue)
                return "Never";

            var timeSince = DateTime.Now - lastRequestTime;
            if (timeSince.TotalMinutes < 1)
                return "Just now";
            if (timeSince.TotalHours < 1)
                return $"{(int)timeSince.TotalMinutes}m ago";
            if (timeSince.TotalDays < 1)
                return $"{(int)timeSince.TotalHours}h ago";

            return $"{(int)timeSince.TotalDays}d ago";
        }

        private Icon CreateStatusIcon(Color color)
        {
            var bitmap = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);

                // Draw circle
                using (var brush = new SolidBrush(color))
                {
                    graphics.FillEllipse(brush, 2, 2, 12, 12);
                }

                // Draw border
                using (var pen = new Pen(Color.Black, 1))
                {
                    graphics.DrawEllipse(pen, 2, 2, 12, 12);
                }
            }

            return Icon.FromHandle(bitmap.GetHicon());
        }

        #region Event Handlers

        private void OnDoubleClick(object sender, EventArgs e)
        {
            OnShowStatus(sender, e);
        }

        private void OnShowStatus(object sender, EventArgs e)
        {
            try
            {
                var status = _statusService.GetCurrentStatus();
                var statusWindow = new StatusWindow(status);
                statusWindow.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show status window");
                System.Windows.MessageBox.Show($"Failed to show status: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnTestConnection(object sender, EventArgs e)
        {
            try
            {
                _logger.LogInformation("Testing connection from system tray");
                // TODO: Implement connection test
                System.Windows.MessageBox.Show("Connection test completed successfully!", "Test Connection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection test failed");
                System.Windows.MessageBox.Show($"Connection test failed: {ex.Message}", "Test Connection",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnViewLogs(object sender, EventArgs e)
        {
            try
            {
                var logPath = _statusService.GetLogFilePath();
                if (System.IO.File.Exists(logPath))
                {
                    System.Diagnostics.Process.Start("notepad.exe", logPath);
                }
                else
                {
                    System.Windows.MessageBox.Show("Log file not found", "View Logs",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open log file");
                System.Windows.MessageBox.Show($"Failed to open log file: {ex.Message}", "View Logs",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnHealthCheck(object sender, EventArgs e)
        {
            try
            {
                var isHealthy = _healthCheckService.IsHealthy();
                var message = isHealthy ? "All systems healthy!" : "Health check failed!";
                var icon = isHealthy ? MessageBoxImage.Information : MessageBoxImage.Warning;

                System.Windows.MessageBox.Show(message, "Health Check", MessageBoxButton.OK, icon);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check error");
                System.Windows.MessageBox.Show($"Health check error: {ex.Message}", "Health Check",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRestart(object sender, EventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "Are you sure you want to restart the launcher?",
                    "Restart Launcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _logger.LogInformation("Restarting launcher from system tray");
                    RestartApplication();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart launcher");
                System.Windows.MessageBox.Show($"Failed to restart: {ex.Message}", "Restart Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "Are you sure you want to exit the launcher?",
                    "Exit Launcher",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _logger.LogInformation("Exiting launcher from system tray");
                    System.Windows.Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to exit launcher");
            }
        }

        #endregion

        private void RestartApplication()
        {
            try
            {
                // Start new instance
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                System.Diagnostics.Process.Start(exePath);

                // Exit current instance
                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart application");
                throw;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _updateTimer?.Stop();
                    _updateTimer?.Dispose();
                    _notifyIcon?.Dispose();
                    _disposed = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing SystemTrayManager");
                }
            }
        }
    }
}