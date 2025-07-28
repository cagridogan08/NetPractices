using AvalonDock;
using AvalonDock.Layout.Serialization;
using AvalonDock.Themes;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace DockingManagerTestApplication
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DockingManager _dockingManager;
        private string _statusText = "Ready";
        private CodeEditorViewModel _activeDocument;
        private readonly string _layoutFilePath = "layout.xml";

        public MainViewModel(DockingManager dockingManager)
        {
            _dockingManager = dockingManager;
            InitializeCommands();
            InitializeTools();
            InitializeDocuments();
            SetupEventHandlers();
        }

        #region Properties

        public ObservableCollection<ToolViewModel> Tools { get; } = new ObservableCollection<ToolViewModel>();
        public ObservableCollection<CodeEditorViewModel> Documents { get; } = new ObservableCollection<CodeEditorViewModel>();

        public SolutionExplorerViewModel SolutionExplorer { get; private set; }
        public PropertiesViewModel Properties { get; private set; }
        public OutputViewModel Output { get; private set; }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public CodeEditorViewModel ActiveDocument
        {
            get => _activeDocument;
            set { _activeDocument = value; OnPropertyChanged(); Properties?.UpdateSelectedItem(value); }
        }

        public Theme CurrentTheme { get; set; }

        #endregion

        #region Commands

        public ICommand NewFileCommand { get; private set; }
        public ICommand OpenFileCommand { get; private set; }
        public ICommand SaveFileCommand { get; private set; }
        public ICommand ExitCommand { get; private set; }
        public ICommand SaveLayoutCommand { get; private set; }
        public ICommand LoadLayoutCommand { get; private set; }
        public ICommand ResetLayoutCommand { get; private set; }

        #endregion

        private void InitializeCommands()
        {
            NewFileCommand = new RelayCommand<object?>(CreateNewFile);
            OpenFileCommand = new RelayCommand<object>(OpenFile);
            SaveFileCommand = new RelayCommand<object>(SaveFile);
            ExitCommand = new RelayCommand(() => System.Windows.Application.Current.Shutdown());
            SaveLayoutCommand = new RelayCommand(SaveLayout);
            LoadLayoutCommand = new RelayCommand(() => LoadLayout());
            ResetLayoutCommand = new RelayCommand(() => ResetLayout());
        }

        private void InitializeTools()
        {
            SolutionExplorer = new SolutionExplorerViewModel();
            Properties = new PropertiesViewModel();
            Output = new OutputViewModel();

            Tools.Add(SolutionExplorer);
            Tools.Add(Properties);
            Tools.Add(Output);

            SolutionExplorer.FileSelected += OnFileSelected;
        }

        private void InitializeDocuments()
        {
            // Create a sample document
            var welcomeDoc = new CodeEditorViewModel("Welcome.txt", "Welcome to AvalonDock Example!\n\nThis is a sample document.");
            Documents.Add(welcomeDoc);
            ActiveDocument = welcomeDoc;
        }

        private void SetupEventHandlers()
        {
            _dockingManager.DocumentClosing += OnDocumentClosing;
            _dockingManager.ActiveContentChanged += OnActiveContentChanged;
        }

        private void CreateNewFile(object parameter)
        {
            var counter = Documents.Count + 1;
            var newDoc = new CodeEditorViewModel($"Document{counter}.txt", "");
            Documents.Add(newDoc);
            ActiveDocument = newDoc;
            StatusText = $"Created new document: {newDoc.Title}";
        }

        private void OpenFile(object parameter)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|C# files (*.cs)|*.cs|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var content = File.ReadAllText(dialog.FileName);
                    var filename = Path.GetFileName(dialog.FileName);
                    var doc = new CodeEditorViewModel(filename, content) { FilePath = dialog.FileName };
                    Documents.Add(doc);
                    ActiveDocument = doc;
                    StatusText = $"Opened: {filename}";
                    Output.AddMessage($"File opened: {dialog.FileName}");
                }
                catch (Exception ex)
                {
                    StatusText = $"Error opening file: {ex.Message}";
                    Output.AddMessage($"Error: {ex.Message}");
                }
            }
        }

        private void SaveFile(object parameter)
        {
            if (ActiveDocument == null) return;

            if (string.IsNullOrEmpty(ActiveDocument.FilePath))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|C# files (*.cs)|*.cs|All files (*.*)|*.*",
                    FileName = ActiveDocument.Title
                };

                if (dialog.ShowDialog() == true)
                {
                    ActiveDocument.FilePath = dialog.FileName;
                }
                else return;
            }

            try
            {
                File.WriteAllText(ActiveDocument.FilePath, ActiveDocument.Content);
                ActiveDocument.IsDirty = false;
                StatusText = $"Saved: {Path.GetFileName(ActiveDocument.FilePath)}";
                Output.AddMessage($"File saved: {ActiveDocument.FilePath}");
            }
            catch (Exception ex)
            {
                StatusText = $"Error saving file: {ex.Message}";
                Output.AddMessage($"Error: {ex.Message}");
            }
        }

        private void OnFileSelected(string filePath)
        {
            // Check if file is already open
            var existingDoc = Documents.FirstOrDefault(d => d.FilePath == filePath);
            if (existingDoc != null)
            {
                ActiveDocument = existingDoc;
                return;
            }

            // Open new file
            try
            {
                var content = File.ReadAllText(filePath);
                var filename = Path.GetFileName(filePath);
                var doc = new CodeEditorViewModel(filename, content) { FilePath = filePath };
                Documents.Add(doc);
                ActiveDocument = doc;
            }
            catch (Exception ex)
            {
                Output.AddMessage($"Error opening file: {ex.Message}");
            }
        }

        private void OnDocumentClosing(object sender, AvalonDock.DocumentClosingEventArgs e)
        {
            if (e.Document.Content is CodeEditorViewModel doc && doc.IsDirty)
            {
                var result = System.Windows.MessageBox.Show(
                    $"Save changes to {doc.Title}?",
                    "Unsaved Changes",
                    System.Windows.MessageBoxButton.YesNoCancel);

                switch (result)
                {
                    case System.Windows.MessageBoxResult.Yes:
                        SaveFile(null);
                        break;
                    case System.Windows.MessageBoxResult.Cancel:
                        e.Cancel = true;
                        return;
                }
            }

            if (!e.Cancel && e.Document.Content is CodeEditorViewModel docToRemove)
            {
                Documents.Remove(docToRemove);
            }
        }

        private void OnActiveContentChanged(object sender, EventArgs e)
        {
            if (_dockingManager.ActiveContent is CodeEditorViewModel doc)
            {
                ActiveDocument = doc;
            }
        }

        private void SaveLayout()
        {
            try
            {
                var serializer = new XmlLayoutSerializer(_dockingManager);
                using (var writer = new StreamWriter(_layoutFilePath))
                {
                    serializer.Serialize(writer);
                }
                StatusText = "Layout saved";
                Output.AddMessage("Layout saved successfully");
            }
            catch (Exception ex)
            {
                StatusText = $"Error saving layout: {ex.Message}";
                Output.AddMessage($"Error saving layout: {ex.Message}");
            }
        }

        private void LoadLayout()
        {
            try
            {
                if (!File.Exists(_layoutFilePath))
                {
                    StatusText = "No saved layout found";
                    return;
                }

                var serializer = new XmlLayoutSerializer(_dockingManager);
                serializer.LayoutSerializationCallback += OnLayoutSerialization;

                using (var reader = new StreamReader(_layoutFilePath))
                {
                    serializer.Deserialize(reader);
                }

                StatusText = "Layout loaded";
                Output.AddMessage("Layout loaded successfully");
            }
            catch (Exception ex)
            {
                StatusText = $"Error loading layout: {ex.Message}";
                Output.AddMessage($"Error loading layout: {ex.Message}");
            }
        }

        private void OnLayoutSerialization(object sender, LayoutSerializationCallbackEventArgs e)
        {
            e.Content = e.Model.ContentId switch
            {
                "SolutionExplorer" => SolutionExplorer,
                "Properties" => Properties,
                "Output" => Output,
                _ => e.Content
            };
        }

        private void ResetLayout()
        {
            // This would reset to default layout - implementation depends on your needs
            StatusText = "Layout reset to default";
            Output.AddMessage("Layout reset to default");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
