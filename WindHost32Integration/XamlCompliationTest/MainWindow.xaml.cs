using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.CSharp;
using Assembly = System.Reflection.Assembly;
using Path = System.IO.Path;

Assembly CompileWpfAssembly(string[] sourceFiles, string[] xamlFiles, string outputPath)
{
    // Step 1: Generate .g.cs files from XAML
    GenerateXamlCode(xamlFiles, outputPath);

    // Step 2: Compile with generated files
    var provider = new CSharpCodeProvider();
    var parameters = new CompilerParameters
    {
        OutputAssembly = Path.Combine(outputPath, "MyApp.exe"),
        GenerateExecutable = true,
        IncludeDebugInformation = false,
        CompilerOptions = "/target:winexe"
    };

    // Add WPF references
    parameters.ReferencedAssemblies.Add("PresentationCore.dll");
    parameters.ReferencedAssemblies.Add("PresentationFramework.dll");
    parameters.ReferencedAssemblies.Add("WindowsBase.dll");
    parameters.ReferencedAssemblies.Add("System.Xaml.dll");

    // Include generated .g.cs files
    var allSourceFiles = sourceFiles.Concat(Directory.GetFiles(outputPath, "*.g.cs")).ToArray();

    var results = provider.CompileAssemblyFromFile(parameters, allSourceFiles);

    if (results.Errors.HasErrors)
    {
        foreach (CompilerError error in results.Errors)
            Console.WriteLine(error.ToString());
        return null;
    }

    return Assembly.LoadFrom(results.PathToAssembly);
}

void GenerateXamlCode(string[] xamlFiles, string outputPath)
{
    foreach (var xamlFile in xamlFiles)
    {
        // Use MSBuild's MarkupCompilePass1 task equivalent
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "msbuild",
            Arguments = $"/t:MarkupCompilePass1 /p:XamlFile={xamlFile} /p:OutputPath={outputPath}",
            UseShellExecute = false,
            RedirectStandardOutput = true
        };

        Process.Start(psi)?.WaitForExit();
    }
}

namespace XamlCompliationTest
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            _ = new XamlCompilationExample();
        }

        public static void CompileWpfWithResources(string projectPath)
        {
            var buildParameters = new BuildParameters()
            {
                Loggers = new[] { new ConsoleLogger(LoggerVerbosity.Normal) }
            };

            var buildRequest = new BuildRequestData(
                projectPath,
                new Dictionary<string, string>(),
                null,
                new[] { "Build" },
                null);

            var buildManager = BuildManager.DefaultBuildManager;
            var result = buildManager.Build(buildParameters, buildRequest);

            Console.WriteLine($"Build succeeded: {result.OverallResult == BuildResultCode.Success}");
        }
    }

    public class XamlCompilationExample
    {
        public XamlCompilationExample()
        {
            // Sample C# code that uses XAML resources
            var csharpCode = @"
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
using System.Windows.Shell;

namespace MyApp
{
    public class MainWindow : Window
    {
private readonly string _content;
        public MainWindow(string content)
        {
            _content = content;
            InitializeComponent();
        }
        
private void InitializeComponent(){

            // Load XAML from embedded resource
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = ""MyApp.MainWindow.xaml""; // Adjust the namespace and class name as needed
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream))
                    {
                        var xamlString = reader.ReadToEnd();
                        using (var stringReader = new StringReader(xamlString))
                        using (var xmlReader = XmlReader.Create(stringReader))
                        {
                            var xamlContent = XamlReader.Load(xmlReader) as FrameworkElement;
                            if (xamlContent != null)
                            {
                                Content = xamlContent;
                            }
                        }
                    }
                }
            }
}
       
        
        public void ShowMessage()
        {
            Console.WriteLine(""Hello from compiled assembly with XAML resources!"");
        }
    }
}";

            // Sample XAML content
            var xamlContent = @"<Window x:Class=""MyApp.MainWindow""
        xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""My Application"" Height=""350"" Width=""525"">
    <Grid>
        <StackPanel Margin=""10"">
            <TextBlock Text=""Welcome to My Application"" FontSize=""16"" FontWeight=""Bold"" HorizontalAlignment=""Center""/>
            <Button Content=""Click Me"" Margin=""0,10,0,0"" Padding=""10,5""/>
            <TextBox Text=""Sample text box"" Margin=""0,10,0,0""/>
        </StackPanel>
    </Grid>
</Window>";

            // Additional resource - a simple text file
            var configContent = @"{
    ""appName"": ""MyWPFApp"",
    ""version"": ""1.0.0"",
    ""theme"": ""dark""
}";

            try
            {
                var assemblyBytes = CompileWithXamlResources(csharpCode, xamlContent, configContent);

                if (assemblyBytes != null)
                {
                    Console.WriteLine($"Successfully compiled assembly with {assemblyBytes.Length} bytes");

                    // Save and test the assembly
                    var assemblyPath = "CompiledWithXaml.dll";
                    File.WriteAllBytes(assemblyPath, assemblyBytes);

                    TestCompiledAssembly(assemblyPath);
                }
                else
                {
                    Console.WriteLine("Compilation failed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        public static byte[] CompileWithXamlResources(string csharpCode, string xamlContent, string configContent)
        {
            // Get required references
            var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Uri).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Assembly).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.IO.File).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(StreamReader).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),

        };

            // Add WPF references if available (optional - for this example)
            try
            {
                references.Add(MetadataReference.CreateFromFile(Assembly.Load("PresentationCore").Location));
                references.Add(MetadataReference.CreateFromFile(Assembly.Load("PresentationFramework").Location));
                references.Add(MetadataReference.CreateFromFile(Assembly.Load("WindowsBase").Location));
            }
            catch
            {
                Console.WriteLine("WPF assemblies not available - continuing without them");
            }

            // Create compilation
            var compilation = CSharpCompilation.Create(
                assemblyName: "MyAppWithXaml",
                syntaxTrees: new[] { CSharpSyntaxTree.ParseText(csharpCode) },
                references: references,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            // Create embedded resources
            var embeddedResources = new List<ResourceDescription>();

            // Add XAML resource
            var xamlBytes = Encoding.UTF8.GetBytes(xamlContent);
            var xamlStream = new MemoryStream(xamlBytes);
            embeddedResources.Add(new ResourceDescription(
                resourceName: "MyApp.MainWindow.xaml",
                dataProvider: () => xamlStream,
                isPublic: false));

            // Add config resource
            var configBytes = Encoding.UTF8.GetBytes(configContent);
            var configStream = new MemoryStream(configBytes);
            embeddedResources.Add(new ResourceDescription(
                resourceName: "MyApp.config.json",
                dataProvider: () => configStream,
                isPublic: false));

            // Add a binary resource example (simulated image data)
            var imageBytes = Encoding.UTF8.GetBytes("FAKE_IMAGE_DATA_HERE");
            var imageStream = new MemoryStream(imageBytes);
            embeddedResources.Add(new ResourceDescription(
                resourceName: "MyApp.Resources.logo.png",
                dataProvider: () => imageStream,
                isPublic: false));

            // Compile with resources
            using var outputStream = new MemoryStream();

            var emitOptions = new EmitOptions(
                debugInformationFormat: DebugInformationFormat.Embedded,
                includePrivateMembers: true);

            var emitResult = compilation.Emit(
                peStream: outputStream,
                manifestResources: embeddedResources,
                options: emitOptions);

            if (!emitResult.Success)
            {
                Console.WriteLine("Compilation failed with errors:");
                foreach (var diagnostic in emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    Console.WriteLine($"  {diagnostic.GetMessage()}");
                }
                return null;
            }

            Console.WriteLine("Compilation successful!");

            // Show embedded resources
            Console.WriteLine($"Embedded {embeddedResources.Count} resources:");
            foreach (var resource in embeddedResources)
            {
                Console.WriteLine($"  - {resource}");
            }

            return outputStream.ToArray();
        }

        public static void TestCompiledAssembly(string assemblyPath)
        {
            try
            {
                // Load the compiled assembly
                var assembly = Assembly.LoadFrom(assemblyPath);

                Console.WriteLine($"\nTesting compiled assembly: {assembly.FullName}");

                // List embedded resources
                var resourceNames = assembly.GetManifestResourceNames();
                Console.WriteLine($"Found {resourceNames.Length} embedded resources:");
                var uris = new List<Uri>();
                foreach (var resourceName in resourceNames)
                {
                    Console.WriteLine($"  - {resourceName}");

                    // Read and display a portion of each resource
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        var content = new StreamReader(stream).ReadToEnd();
                        var preview = content.Length > 100 ? content.Substring(0, 100) + "..." : content;

                        Console.WriteLine($"    Content preview: {preview.Replace('\n', ' ').Replace('\r', ' ')}");
                    }
                }

                // Create and test an instance
                var mainWindowType = assembly.GetType("MyApp.MainWindow");
                if (mainWindowType != null)
                {
                    var instance = Activator.CreateInstance(mainWindowType);
                    var xamlResourceName = resourceNames.FirstOrDefault(r => r.EndsWith(".xaml"));
                    if (xamlResourceName != null && instance is Window win)
                    {
                        using var stream = assembly.GetManifestResourceStream(xamlResourceName);
                        if (stream != null)
                        {
                            using var reader = new StreamReader(stream);
                            var xamlString = reader.ReadToEnd();

                            try
                            {
                                using var stringReader = new StringReader(xamlString);
                                using var xmlReader = XmlReader.Create(stringReader);
                                var xamlContent = XamlReader.Load(xmlReader);

                                if (xamlContent is FrameworkElement element)
                                {
                                    win.Content = element;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error parsing XAML: {ex.Message}");
                            }
                        }
                    }
                    var showMessageMethod = mainWindowType.GetMethod("ShowMessage");
                    showMessageMethod?.Invoke(instance, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error testing assembly: {ex.Message}");
            }
        }
    }
}