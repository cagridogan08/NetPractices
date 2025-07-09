using System;
using System.Windows;
using ProjectExporter.Models;

namespace ProjectExporter
{
    public partial class CreateVersionWindow : Window
    {
        private readonly ProjectInfo _baseProject;

        public string NewVersion { get; private set; }
        public string Description { get; private set; }

        public CreateVersionWindow(ProjectInfo baseProject)
        {
            InitializeComponent();
            _baseProject = baseProject;

            BaseProjectText.Text = $"Base project: {baseProject.Name} (v{baseProject.Version})";

            // Auto-suggest next version
            if (Version.TryParse(baseProject.Version, out var currentVersion))
            {
                var nextVersion = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build + 1);
                VersionTextBox.Text = nextVersion.ToString();
            }
            else
            {
                VersionTextBox.Text = "1.0.1";
            }

            VersionTextBox.TextChanged += OnInputChanged;
            DescriptionTextBox.TextChanged += OnInputChanged;

            UpdatePreview();
        }

        private void OnInputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var version = string.IsNullOrWhiteSpace(VersionTextBox.Text) ? "[Version]" : VersionTextBox.Text;
            var description = string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ? "[No description]" : DescriptionTextBox.Text;

            PreviewText.Text = $"Project: {_baseProject.BaseProjectName ?? _baseProject.Name}\n" +
                             $"New Version: {version}\n" +
                             $"Description: {description}";

            CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(VersionTextBox.Text);
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(VersionTextBox.Text))
            {
                MessageBox.Show("Please enter a version number.", "Validation Error",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
                VersionTextBox.Focus();
                return;
            }

            // Basic version validation
            if (!System.Text.RegularExpressions.Regex.IsMatch(VersionTextBox.Text, @"^\d+\.\d+\.\d+$"))
            {
                var result = MessageBox.Show("Version format should be like '1.0.0'. Continue anyway?",
                                           "Version Format", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                {
                    VersionTextBox.Focus();
                    return;
                }
            }

            NewVersion = VersionTextBox.Text.Trim();
            Description = DescriptionTextBox.Text.Trim();

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