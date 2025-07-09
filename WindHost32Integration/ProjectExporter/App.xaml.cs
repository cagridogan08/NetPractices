using System.Configuration;
using System.Data;
using System.Windows;

namespace ProjectExporter
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling
            DispatcherUnhandledException += (sender, ex) =>
            {
                MessageBox.Show($"An unexpected error occurred: {ex.Exception.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }

}
