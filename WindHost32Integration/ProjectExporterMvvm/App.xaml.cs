using Microsoft.Extensions.Hosting;
using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ProjectExporter.Services;
using ProjectExporterMvvm.ViewModels;

namespace ProjectExporterMvvm
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Build host with dependency injection
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    ConfigureServices(services);
                })
                .Build();

            // Set up global exception handling
            DispatcherUnhandledException += (sender, ex) =>
            {
                MessageBox.Show($"An unexpected error occurred: {ex.Exception.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // Start the application
            StartApplication();

            base.OnStartup(e);
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // Core Services
            services.AddSingleton<IMessenger, Messenger>();
            services.AddSingleton<IViewModelFactory, ViewModelFactory>();
            services.AddSingleton<IWindowService, WindowService>();

            // Application Services
            services.AddSingleton<IApiService, ApiService>();
            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddTransient<IDialogService, DialogService>();

            // ViewModels (Transient - new instance each time)
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<CreateVersionWindowViewModel>();
            services.AddTransient<ServerSettingsWindowViewModel>();
            services.AddTransient<VersionConflictWindowViewModel>();
            services.AddTransient<AddPresetWindowViewModel>();
            services.AddTransient<VersionsWindowViewModel>();

            // Windows (Transient - new instance each time)
            services.AddTransient<MainWindow>();
            services.AddTransient<CreateVersionWindow>();
            services.AddTransient<ServerSettingsWindow>();
            services.AddTransient<VersionConflictWindow>();
            services.AddTransient<AddPresetWindow>();
            services.AddTransient<VersionsWindow>();
        }

        private void StartApplication()
        {
            var windowService = _host.Services.GetRequiredService<IWindowService>();
            var viewModelFactory = _host.Services.GetRequiredService<IViewModelFactory>();
            var messenger = _host.Services.GetRequiredService<IMessenger>();

            // Set up message handlers
            SetupMessageHandlers(messenger);

            // Create and show main window
            var mainViewModel = viewModelFactory.Create<MainWindowViewModel>();
            windowService.ShowWindow<MainWindow>(mainViewModel);
        }

        private void SetupMessageHandlers(IMessenger messenger)
        {
            var windowService = _host.Services.GetRequiredService<IWindowService>();
            var viewModelFactory = _host.Services.GetRequiredService<IViewModelFactory>();

            // Handle dialog messages
            messenger.Subscribe<ShowMessageDialog>(msg =>
            {
                MessageBox.Show(msg.Message, msg.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                msg.Callback?.Invoke(true);
            });

            messenger.Subscribe<ShowErrorDialog>(msg =>
            {
                MessageBox.Show(msg.Message, msg.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                msg.Callback?.Invoke(true);
            });

            messenger.Subscribe<ShowConfirmationDialog>(msg =>
            {
                var result = MessageBox.Show(msg.Message, msg.Title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                msg.Callback?.Invoke(result == MessageBoxResult.Yes);
            });

            messenger.Subscribe<OpenFileDialog>(msg =>
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = msg.Filter,
                    Title = msg.Title
                };
                var result = openFileDialog.ShowDialog();
                msg.Callback?.Invoke(result == true ? openFileDialog.FileName : null);
            });

            messenger.Subscribe<ShowCreateVersionDialog>(msg =>
            {
                var viewModel = viewModelFactory.Create<CreateVersionWindowViewModel>();
                viewModel.Initialize(msg.BaseProject);
                var result = windowService.ShowDialog<CreateVersionWindow>(viewModel);
                msg.Callback?.Invoke(viewModel.NewVersion, viewModel.Description, result == true);
            });

            messenger.Subscribe<ShowVersionConflictDialog>(msg =>
            {
                var viewModel = viewModelFactory.Create<VersionConflictWindowViewModel>();
                viewModel.Initialize(msg.Conflict);
                var result = windowService.ShowDialog<VersionConflictWindow>(viewModel);
                msg.Callback?.Invoke(viewModel.ShouldOverride, viewModel.FinalSelectedVersion);
            });

            messenger.Subscribe<ShowServerSettingsDialog>(msg =>
            {
                var viewModel = viewModelFactory.Create<ServerSettingsWindowViewModel>();
                viewModel.Initialize(msg.CurrentServer, msg.RecentServers);
                var result = windowService.ShowDialog<ServerSettingsWindow>(viewModel);
                msg.Callback?.Invoke(viewModel.SelectedServer, result == true);
            });

            messenger.Subscribe<ShowProjectVersionsDialog>(msg =>
            {
                var viewModel = viewModelFactory.Create<VersionsWindowViewModel>();
                viewModel.Initialize(msg.Project);
                windowService.ShowWindow<VersionsWindow>(viewModel);
            });

            messenger.Subscribe<NavigateToWindow>(msg =>
            {
                if (msg.IsDialog)
                {
                    // Use reflection to call ShowDialog with proper type
                    var method = typeof(IWindowService).GetMethod(nameof(IWindowService.ShowDialog), new[] { typeof(object) });
                    var genericMethod = method.MakeGenericMethod(msg.WindowType);
                    genericMethod.Invoke(windowService, new[] { msg.DataContext });
                }
                else
                {
                    var method = typeof(IWindowService).GetMethod(nameof(IWindowService.ShowWindow), new[] { typeof(object) });
                    var genericMethod = method.MakeGenericMethod(msg.WindowType);
                    genericMethod.Invoke(windowService, new[] { msg.DataContext });
                }
            });

            messenger.Subscribe<CloseWindow>(msg =>
            {
                var method = typeof(IWindowService).GetMethod(nameof(IWindowService.CloseWindow));
                var genericMethod = method.MakeGenericMethod(msg.WindowType);
                genericMethod.Invoke(windowService, new object[] { msg.DialogResult });
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
