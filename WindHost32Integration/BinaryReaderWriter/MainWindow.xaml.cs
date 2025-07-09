using System.IO.Compression;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace BinaryReaderWriter
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json;*.bjson)|*.json;*.bjson|All files (*.*)|*.*",
                Title = "Open JSON File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string jsonContent = await JsonHelper.ReadBinaryJsonAsync(openFileDialog.FileName);
                    if (jsonContent != null)
                    {
                        txtJsonContent.Text = jsonContent;
                        txtFilePath.Text = openFileDialog.FileName;
                        MessageBox.Show("File loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to read the file content.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtJsonContent.Text))
            {
                MessageBox.Show("Please enter some JSON content to save.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Binary JSON Files (*.bjson)|*.bjson|JSON Files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".bjson",
                Title = "Save JSON File"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    bool success = await JsonHelper.WriteBinaryJsonAsync(saveFileDialog.FileName, txtJsonContent.Text);
                    if (success)
                    {
                        txtFilePath.Text = saveFileDialog.FileName;
                        MessageBox.Show("File saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to save the file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            txtJsonContent.Clear();
            txtFilePath.Clear();
        }

        private void BtnSampleJson_Click(object sender, RoutedEventArgs e)
        {
            txtJsonContent.Text = @"{
  ""name"": ""Sample Data"",
  ""created"": ""2025-05-08T10:30:00"",
  ""isActive"": true,
  ""count"": 42,
  ""items"": [
    {
      ""id"": 1,
      ""value"": ""First item""
    },
    {
      ""id"": 2,
      ""value"": ""Second item""
    },
    {
      ""id"": 3,
      ""value"": ""Third item""
    }
  ],
  ""settings"": {
    ""theme"": ""dark"",
    ""fontSize"": 14,
    ""notifications"": true
  }
}";
        }

        private void BtnValidate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var jsonObject = JsonConvert.DeserializeObject(txtJsonContent.Text);
                string formattedJson = JsonConvert.SerializeObject(jsonObject, Formatting.Indented);
                txtJsonContent.Text = formattedJson;
                MessageBox.Show("JSON is valid and has been formatted.", "Validation Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid JSON: {ex.Message}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public static class JsonHelper
    {
        /// <summary>
        /// Reads a compressed JSON file and returns its contents as a string.
        /// If the file is not in the expected compressed format, attempts to read it as plain text
        /// and then rewrite it in the compressed format.
        /// </summary>
        public static string ReadBinaryJson(string filePath)
        {
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                    {
                        using (StreamReader streamReader = new StreamReader(gzipStream, Encoding.UTF8))
                            return streamReader.ReadToEnd();
                    }
                }
            }
            catch (InvalidDataException ex1)
            {
                LogException("JsonHelper.ReadBinaryJson", ex1);
                try
                {
                    string jsonString = File.ReadAllText(filePath);
                    WriteBinaryJson(filePath, jsonString);
                    return jsonString;
                }
                catch (Exception ex2)
                {
                    LogException("JsonHelper.ReadBinaryJson fallback", ex2);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogException("JsonHelper.ReadBinaryJson", ex);
                return null;
            }
        }

        /// <summary>
        /// Writes a JSON string to a file in a compressed format.
        /// </summary>
        public static bool WriteBinaryJson(string filePath, string jsonString)
        {
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
                    {
                        using (StreamWriter streamWriter = new StreamWriter(gzipStream, Encoding.UTF8))
                        {
                            streamWriter.Write(jsonString);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogException("JsonHelper.WriteBinaryJson", ex);
                return false;
            }
        }

        /// <summary>
        /// Asynchronously reads a compressed JSON file.
        /// </summary>
        public static async Task<string> ReadBinaryJsonAsync(string filePath)
        {
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                    {
                        using (StreamReader streamReader = new StreamReader(gzipStream, Encoding.UTF8))
                            return await streamReader.ReadToEndAsync();
                    }
                }
            }
            catch (InvalidDataException ex1)
            {
                LogException("JsonHelper.ReadBinaryJsonAsync", ex1);
                try
                {
                    string jsonString = await File.ReadAllTextAsync(filePath);
                    await WriteBinaryJsonAsync(filePath, jsonString);
                    return jsonString;
                }
                catch (Exception ex2)
                {
                    LogException("JsonHelper.ReadBinaryJsonAsync fallback", ex2);
                }
                return null;
            }
            catch (Exception ex)
            {
                LogException("JsonHelper.ReadBinaryJsonAsync", ex);
                return null;
            }
        }

        /// <summary>
        /// Asynchronously writes a JSON string to a file in a compressed format.
        /// </summary>
        public static async Task<bool> WriteBinaryJsonAsync(string filePath, string jsonString)
        {
            try
            {
                using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Compress))
                    {
                        using (StreamWriter streamWriter = new StreamWriter(gzipStream, Encoding.UTF8))
                        {
                            await streamWriter.WriteAsync(jsonString);
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                LogException("JsonHelper.WriteBinaryJsonAsync", ex);
                return false;
            }
        }

        /// <summary>
        /// Simple logging method. Replace with your actual logging implementation.
        /// </summary>
        private static void LogException(string source, Exception ex)
        {
            Console.WriteLine($"Error in {source}: {ex.Message}");
            // Replace with your actual logging implementation
        }
    }
}