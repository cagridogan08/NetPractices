using System.Windows;

namespace ProjectExporterMvvm.Views
{
    /// <summary>
    /// Interaction logic for AddPresetWindow.xaml
    /// </summary>
    public partial class AddPresetWindow : Window
    {
        public string PresetUrl { get; private set; }

        public AddPresetWindow()
        {
            InitializeComponent();
            PresetUrlTextBox.Focus();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            var url = PresetUrlTextBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Please enter a server URL.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PresetUrlTextBox.Focus();
                return;
            }

            PresetUrl = url;
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
