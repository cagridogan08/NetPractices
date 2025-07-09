using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Windows;
using System.Xml;
using Microsoft.CSharp;

class Program
{
    [STAThread]
    static void Main()
    {
        // Define your C# source code
        var csharpCode = new[]
        {
            @"
using System;
using System.Windows;
using System.Windows.Controls;

namespace MyWpfApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Title = ""Dynamically Compiled WPF App"";
        }
        
        public void ShowMessage()
        {
            MessageBox.Show(""Hello from dynamically compiled WPF with .g.resources!"", ""Success"");
        }
        
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            ShowMessage();
        }
    }
    
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            var mainWindow = new MainWindow();
            app.Run(mainWindow);
        }
    }
}",
            @"
using System.Windows;

namespace MyWpfApp
{
    public partial class SecondWindow : Window
    {
        public SecondWindow()
        {
            InitializeComponent();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}"
        };

        // Define your XAML files
        var xamlFiles = new Dictionary<string, string>
        {
            ["MainWindow.xaml"] = @"
<Window x:Class=""MyWpfApp.MainWindow""
        xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Main Window"" Height=""350"" Width=""525"">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height=""Auto""/>
            <RowDefinition Height=""*""/>
            <RowDefinition Height=""Auto""/>
        </Grid.RowDefinitions>
        
        <TextBlock Grid.Row=""0"" Text=""Dynamic WPF Application"" 
                   FontSize=""20"" FontWeight=""Bold"" 
                   HorizontalAlignment=""Center"" Margin=""10""/>
        
        <StackPanel Grid.Row=""1"" Orientation=""Vertical"" 
                    HorizontalAlignment=""Center"" VerticalAlignment=""Center"">
            <Button Name=""btnShowMessage"" Content=""Show Message"" 
                    Width=""150"" Height=""30"" Margin=""5"" 
                    Click=""Button_Click""/>
            <Button Name=""btnOpenSecond"" Content=""Open Second Window"" 
                    Width=""150"" Height=""30"" Margin=""5"" 
                    Click=""OpenSecond_Click""/>
        </StackPanel>
        
        <TextBlock Grid.Row=""2"" Text=""Compiled from memory with .g.resources"" 
                   HorizontalAlignment=""Center"" Margin=""5"" 
                   FontStyle=""Italic"" Foreground=""Gray""/>
    </Grid>
</Window>",

            ["SecondWindow.xaml"] = @"
<Window x:Class=""MyWpfApp.SecondWindow""
        xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Second Window"" Height=""200"" Width=""300"">
    <Grid>
        <StackPanel HorizontalAlignment=""Center"" VerticalAlignment=""Center"">
            <TextBlock Text=""This is the second window!"" 
                       FontSize=""16"" HorizontalAlignment=""Center"" Margin=""10""/>
            <Button Name=""btnClose"" Content=""Close"" 
                    Width=""100"" Height=""30"" 
                    Click=""CloseButton_Click""/>
        </StackPanel>
    </Grid>
</Window>"
        };

        try
        {
            Console.WriteLine("Compiling WPF application from memory...");

            // Compile the WPF application
            var assembly = InMemoryWpfCompiler.CompileWpfFromCode(csharpCode, xamlFiles);

            Console.WriteLine("Compilation successful!");
            Console.WriteLine("Checking embedded resources...");

            // Verify that .g.resources was created
            var resourceNames = assembly.GetManifestResourceNames();
            foreach (var resourceName in resourceNames)
            {
                Console.WriteLine($"  - Embedded resource: {resourceName}");
            }

            // Test creating and using the compiled window
            var mainWindowType = assembly.GetType("MyWpfApp.MainWindow");
            if (mainWindowType != null)
            {
                Console.WriteLine("Creating MainWindow instance...");
                var mainWindow = Activator.CreateInstance(mainWindowType);

                // Call ShowMessage method
                var showMessageMethod = mainWindowType.GetMethod("ShowMessage");
                Console.WriteLine("Calling ShowMessage method...");
                showMessageMethod?.Invoke(mainWindow, null);

                Console.WriteLine("Success! WPF application compiled and executed.");
            }
            else
            {
                Console.WriteLine("Error: Could not find MainWindow type in compiled assembly.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during compilation: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}

public class InMemoryWpfCompiler
{
    public static Assembly CompileWpfFromCode(string[] csharpCode, Dictionary<string, string> xamlFiles)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WpfCompiler_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            Console.WriteLine($"Using temporary directory: {tempDir}");

            // Step 1: Generate .g.cs files from XAML
            Console.WriteLine("Generating code from XAML...");
            var generatedSources = GenerateCodeFromXaml(xamlFiles, tempDir);

            // Step 2: Create .g.resources file
            Console.WriteLine("Creating .g.resources file...");
            var resourcesFile = CreateResourcesFile(xamlFiles, tempDir);

            // Step 3: Compile everything together
            Console.WriteLine("Compiling assembly...");
            return CompileWithResources(csharpCode, generatedSources, resourcesFile, tempDir);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
                Console.WriteLine("Cleaned up temporary directory.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not clean up temp directory: {ex.Message}");
            }
        }
    }

    private static List<string> GenerateCodeFromXaml(Dictionary<string, string> xamlFiles, string tempDir)
    {
        var generatedSources = new List<string>();

        foreach (var kvp in xamlFiles)
        {
            var fileName = kvp.Key; // e.g., "MainWindow.xaml"
            var xamlContent = kvp.Value;
            var className = Path.GetFileNameWithoutExtension(fileName);

            Console.WriteLine($"  Processing {fileName}...");

            // Parse XAML to extract namespace and class info
            var xamlInfo = ParseXamlInfo(xamlContent, fileName);

            var generatedCode = $@"
// <auto-generated>
//     This code was generated by InMemoryWpfCompiler.
// </auto-generated>

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace {xamlInfo.Namespace} 
{{
    public partial class {className} : {xamlInfo.BaseClass}, System.Windows.Markup.IComponentConnector 
    {{
        private bool _contentLoaded;
        
        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute(""PresentationBuildTasks"", ""4.0.0.0"")]
        public void InitializeComponent() 
        {{
            if (_contentLoaded) 
            {{
                return;
            }}
            _contentLoaded = true;
            System.Uri resourceLocater = new System.Uri(""/{xamlInfo.AssemblyName};component/{fileName}"", System.UriKind.Relative);
            
            System.Windows.Application.LoadComponent(this, resourceLocater);
        }}
        
        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute(""PresentationBuildTasks"", ""4.0.0.0"")]
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute(""Microsoft.Design"", ""CA1033:InterfaceMethodsShouldBeCallableByChildTypes"")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute(""Microsoft.Maintainability"", ""CA1502:AvoidExcessiveComplexity"")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute(""Microsoft.Performance"", ""CA1800:DoNotCastUnnecessarily"")]
        void System.Windows.Markup.IComponentConnector.Connect(int connectionId, object target) 
        {{
            switch (connectionId)
            {{
            case 1:
            
            #line 16 ""{fileName}""
            ((System.Windows.Controls.Button)(target)).Click += new System.Windows.RoutedEventHandler(this.Button_Click);
            
            #line default
            #line hidden
            return;
            case 2:
            
            #line 19 ""{fileName}""
            ((System.Windows.Controls.Button)(target)).Click += new System.Windows.RoutedEventHandler(this.OpenSecond_Click);
            
            #line default
            #line hidden
            return;
            }}
            this._contentLoaded = true;
        }}
        
        // Event handler methods (will be implemented in user code)
        private void OpenSecond_Click(object sender, RoutedEventArgs e)
        {{
            try
            {{
                var secondWindowType = this.GetType().Assembly.GetType(""{xamlInfo.Namespace}.SecondWindow"");
                if (secondWindowType != null)
                {{
                    var secondWindow = Activator.CreateInstance(secondWindowType) as Window;
                    secondWindow?.Show();
                }}
            }}
            catch (Exception ex)
            {{
                MessageBox.Show($""Error opening second window: {{ex.Message}}"");
            }}
        }}
    }}
}}";

            generatedSources.Add(generatedCode);
        }

        return generatedSources;
    }

    private static string CreateResourcesFile(Dictionary<string, string> xamlFiles, string tempDir)
    {
        var resourcesFile = Path.Combine(tempDir, "MyWpfApp.g.resources");

        using (var writer = new ResourceWriter(resourcesFile))
        {
            foreach (var kvp in xamlFiles)
            {
                var fileName = kvp.Key.ToLowerInvariant();
                var xamlContent = kvp.Value;

                Console.WriteLine($"  Adding {fileName} to resources...");

                // Convert XAML string to binary format that WPF expects
                var xamlBytes = Encoding.UTF8.GetBytes(xamlContent);
                writer.AddResource(fileName, xamlBytes);
            }
        }

        Console.WriteLine($"Created resources file: {resourcesFile}");
        return resourcesFile;
    }

    private static Assembly CompileWithResources(string[] csharpCode, List<string> generatedSources,
        string resourcesFile, string tempDir)
    {
        var provider = new CSharpCodeProvider();
        var parameters = new CompilerParameters
        {
            GenerateInMemory = false,
            OutputAssembly = Path.Combine(tempDir, "MyWpfApp.exe"),
            GenerateExecutable = true,
            CompilerOptions = "/target:winexe /unsafe",
            IncludeDebugInformation = false
        };

        // Add WPF and .NET references
        parameters.ReferencedAssemblies.AddRange(new[]
        {
            "mscorlib.dll",
            "System.dll",
            "System.Core.dll",
            "System.Xml.dll",
            "PresentationCore.dll",
            "PresentationFramework.dll",
            "WindowsBase.dll",
            "System.Xaml.dll",
            "UIAutomationProvider.dll",
            "UIAutomationTypes.dll"
        });

        // Embed the resources file
        parameters.EmbeddedResources.Add(resourcesFile);

        // Combine all source code
        var allSources = csharpCode.Concat(generatedSources).ToArray();

        Console.WriteLine($"Compiling {allSources.Length} source files...");
        Console.WriteLine($"Embedding resource file: {resourcesFile}");

        var results = provider.CompileAssemblyFromSource(parameters, allSources);

        if (results.Errors.HasErrors)
        {
            var errors = string.Join("\n", results.Errors.Cast<CompilerError>()
                .Where(e => !e.IsWarning)
                .Select(e => $"Error {e.ErrorNumber}: {e.ErrorText} at line {e.Line}"));
            throw new InvalidOperationException($"Compilation failed:\n{errors}");
        }

        if (results.Errors.HasWarnings)
        {
            Console.WriteLine("Compilation warnings:");
            foreach (CompilerError warning in results.Errors)
            {
                if (warning.IsWarning)
                    Console.WriteLine($"  Warning {warning.ErrorNumber}: {warning.ErrorText}");
            }
        }

        Console.WriteLine($"Assembly compiled successfully: {results.PathToAssembly}");
        return Assembly.LoadFrom(results.PathToAssembly);
    }

    private static XamlInfo ParseXamlInfo(string xamlContent, string fileName)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xamlContent);

            var root = doc.DocumentElement;
            var classAttribute = root.GetAttribute("x:Class");
            var namespaceName = "MyWpfApp";
            var className = Path.GetFileNameWithoutExtension(fileName);

            if (!string.IsNullOrEmpty(classAttribute))
            {
                var parts = classAttribute.Split('.');
                if (parts.Length > 1)
                {
                    namespaceName = string.Join(".", parts.Take(parts.Length - 1));
                    className = parts.Last();
                }
            }

            return new XamlInfo
            {
                Namespace = namespaceName,
                ClassName = className,
                BaseClass = root.LocalName, // Window, UserControl, etc.
                AssemblyName = "MyWpfApp"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not parse XAML info from {fileName}: {ex.Message}");
            return new XamlInfo
            {
                Namespace = "MyWpfApp",
                ClassName = Path.GetFileNameWithoutExtension(fileName),
                BaseClass = "Window",
                AssemblyName = "MyWpfApp"
            };
        }
    }

    private class XamlInfo
    {
        public string Namespace { get; set; }
        public string ClassName { get; set; }
        public string BaseClass { get; set; }
        public string AssemblyName { get; set; }
    }
}