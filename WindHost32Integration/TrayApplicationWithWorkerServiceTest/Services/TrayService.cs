
namespace TrayApplicationWithWorkerServiceTest.Services
{
    public class TrayService : IDisposable
    {
        private readonly ILogger<TrayService> _logger;
        private NotifyIcon? _notifyIcon;
        public TrayService(ILogger<TrayService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogInformation("TrayService initialized.");

        }
        public void Start()
        {
            _logger.LogInformation("TrayService started.");
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.WinLogo,
                Visible = true,
                Text = "Tray Application"
            };
            _notifyIcon.DoubleClick += DoubleClick;
        }

        private void DoubleClick(object? sender, EventArgs e)
        {
            var window = new System.Windows.Forms.Form
            {
                Text = "Tray Application",
                Width = 300,
                Height = 200
            };
            window.Show();
        }

        public void Dispose()
        {
            _logger.LogInformation("TrayService disposed.");
            if (_notifyIcon != null)
            {
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
