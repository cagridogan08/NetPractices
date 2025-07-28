using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace DockingManagerTestApplication
{
    public class SolutionExplorerViewModel : ToolViewModel
    {
        public override string Name => "Solution Explorer";

        public ObservableCollection<FileItem> Files { get; } = new ObservableCollection<FileItem>();
        public ICommand OpenFileCommand { get; }

        public event Action<string> FileSelected;

        public SolutionExplorerViewModel()
        {
            OpenFileCommand = new RelayCommand<string>(OpenFile);
            LoadSampleFiles();
        }

        private void LoadSampleFiles()
        {
            // Add some sample files
            Files.Add(new FileItem { Name = "Program.cs", Path = "Sample/Program.cs", IsFile = true });
            Files.Add(new FileItem { Name = "MainWindow.xaml", Path = "Sample/MainWindow.xaml", IsFile = true });
            Files.Add(new FileItem { Name = "App.config", Path = "Sample/App.config", IsFile = true });
        }

        private void OpenFile(object parameter)
        {
            if (parameter is FileItem fileItem && fileItem.IsFile)
            {
                // In a real application, you'd check if the file exists
                // For demo purposes, we'll create sample content
                var sampleContent = GetSampleContent(fileItem.Name);
                var tempPath = Path.Combine(Path.GetTempPath(), fileItem.Name);
                File.WriteAllText(tempPath, sampleContent);

                FileSelected?.Invoke(tempPath);
            }
        }

        private string GetSampleContent(string fileName)
        {
            return fileName.EndsWith(".cs")
                ? "using System;\n\nnamespace Sample\n{\n    class Program\n    {\n        static void Main(string[] args)\n        {\n            Console.WriteLine(\"Hello, World!\");\n        }\n    }\n}"
                : $"// Sample content for {fileName}\n// This is a demo file created by the AvalonDock example.";
        }
    }

    public class FileItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsFile { get; set; }
    }
}
