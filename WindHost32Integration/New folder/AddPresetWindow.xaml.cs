using System;
using System.Collections.Generic;
using System.Linq;
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
