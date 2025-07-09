using ProjectExporter.Models;
using System.Windows;
using System.Windows.Controls;

namespace ProjectExporter
{
    /// <summary>
    /// Interaction logic for VersionConflictWindow.xaml
    /// </summary>
    public partial class VersionConflictWindow : Window
    {
        private readonly VersionConflictResponse _conflict;

        public bool ShouldOverride { get; private set; }
        public string SelectedVersion { get; private set; }

        public VersionConflictWindow(VersionConflictResponse conflict)
        {
            InitializeComponent();
            _conflict = conflict;

            LoadConflictInfo();
            LoadVersionSuggestions();
            UpdatePreview();

            // Wire up events
            OverrideRadioButton.Checked += (s, e) => UpdatePreview();
            UseNewVersionRadioButton.Checked += (s, e) => UpdatePreview();
        }

        private void LoadConflictInfo()
        {
            ConflictMessageText.Text = _conflict.Message;

            if (_conflict.ExistingProject != null)
            {
                var existing = _conflict.ExistingProject;
                ExistingProjectName.Text = $"Name: {existing.Name}";
                ExistingProjectVersion.Text = $"Version: {existing.Version}";
                ExistingProjectSize.Text = $"Size: {existing.FormattedSize}";
                ExistingProjectCreated.Text = $"Created: {existing.CreatedDate:yyyy-MM-dd HH:mm}";
                ExistingProjectModified.Text = $"Modified: {existing.LastModifiedDate:yyyy-MM-dd HH:mm}";
                ExistingProjectCreatedBy.Text = $"Created By: {existing.Metadata?.CreatedBy ?? "Unknown"}";
            }
        }

        private void LoadVersionSuggestions()
        {
            VersionSuggestionsComboBox.Items.Clear();

            if (_conflict.SuggestedVersions?.Any() == true)
            {
                foreach (var version in _conflict.SuggestedVersions)
                {
                    VersionSuggestionsComboBox.Items.Add(version);
                }

                // Select the first suggestion by default
                VersionSuggestionsComboBox.SelectedIndex = 0;
            }
            else
            {
                // Add some default suggestions
                VersionSuggestionsComboBox.Items.Add("1.0.1");
                VersionSuggestionsComboBox.Items.Add("1.1.0");
                VersionSuggestionsComboBox.Items.Add("2.0.0");
                VersionSuggestionsComboBox.SelectedIndex = 0;
            }
        }

        private void VersionSuggestionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (OverrideRadioButton?.IsChecked == true)
            {
                ActionPreviewText.Text =
                    $"⚠️ OVERRIDE ACTION:\n\n" +
                    $"• The existing project '{_conflict.ConflictingProjectName}' version '{_conflict.ConflictingVersion}' will be DELETED\n" +
                    $"• Your new upload will replace it completely\n" +
                    $"• This action cannot be undone\n\n" +
                    $"Created: {_conflict.ExistingProject?.CreatedDate:yyyy-MM-dd HH:mm}\n" +
                    $"Size: {_conflict.ExistingProject?.FormattedSize}\n" +
                    $"Files: {_conflict.ExistingProject?.FileCount}";
                ActionPreviewText.Foreground = System.Windows.Media.Brushes.DarkRed;
            }
            else if (UseNewVersionRadioButton?.IsChecked == true)
            {
                var selectedVersion = VersionSuggestionsComboBox?.Text ?? "Unknown";
                ActionPreviewText.Text =
                    $"✅ NEW VERSION ACTION:\n\n" +
                    $"• Your project will be uploaded as version '{selectedVersion}'\n" +
                    $"• The existing version '{_conflict.ConflictingVersion}' will remain unchanged\n" +
                    $"• Both versions will be available in the system\n\n" +
                    $"New project name: {_conflict.ConflictingProjectName}_v{selectedVersion}";
                ActionPreviewText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            }
        }

        private void ProceedButton_Click(object sender, RoutedEventArgs e)
        {
            if (OverrideRadioButton.IsChecked == true)
            {
                var result = MessageBox.Show(
                    $"Are you absolutely sure you want to override the existing version?\n\n" +
                    $"This will permanently delete:\n" +
                    $"• Project: {_conflict.ConflictingProjectName}\n" +
                    $"• Version: {_conflict.ConflictingVersion}\n" +
                    $"• Size: {_conflict.ExistingProject?.FormattedSize}\n" +
                    $"• Files: {_conflict.ExistingProject?.FileCount}\n\n" +
                    $"This action cannot be undone!",
                    "Confirm Override",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.Yes)
                {
                    ShouldOverride = true;
                    DialogResult = true;
                    Close();
                }
            }
            else if (UseNewVersionRadioButton.IsChecked == true)
            {
                var selectedVersion = VersionSuggestionsComboBox.Text?.Trim();

                if (string.IsNullOrEmpty(selectedVersion))
                {
                    MessageBox.Show("Please enter or select a version number.", "Validation Error",
                                  MessageBoxButton.OK, MessageBoxImage.Warning);
                    VersionSuggestionsComboBox.Focus();
                    return;
                }

                if (selectedVersion == _conflict.ConflictingVersion)
                {
                    MessageBox.Show("Please choose a different version number than the conflicting one.",
                                  "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    VersionSuggestionsComboBox.Focus();
                    return;
                }

                SelectedVersion = selectedVersion;
                ShouldOverride = false;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
