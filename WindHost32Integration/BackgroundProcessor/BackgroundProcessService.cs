using System.Diagnostics;
using Models;
using Models.SharedLibrary.Models;

public class BackgroundProcessService
{
    private readonly DataTransferManager _dataManager;
    private Process _backgroundProcess;

    public BackgroundProcessService()
    {
        _dataManager = new DataTransferManager();
    }

    public async Task<bool> StartBackgroundProcessAsync()
    {
        try
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackgroundProcessor.exe");

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException($"Background processor not found: {exePath}");
            }

            _backgroundProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            _backgroundProcess.Start();

            // Wait a bit for the process to initialize
            await Task.Delay(1000);

            return !_backgroundProcess.HasExited;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start background process: {ex.Message}");
            return false;
        }
    }

    public async Task<ProcessingResponse> ProcessDataAsync(string operation, object parameters)
    {
        var request = new ProcessingRequest
        {
            Operation = operation,
            Parameters = parameters as Dictionary<string, object> ??
                         new Dictionary<string, object> { ["data"] = parameters }
        };

        return await _dataManager.SendRequestAsync(request);
    }

    public async Task<string> SendLargeDatasetAsync<T>(T data)
    {
        return await _dataManager.WriteDataFileAsync(data);
    }

    public async Task<T> ReceiveLargeDatasetAsync<T>(string fileId)
    {
        var result = await _dataManager.ReadDataFileAsync<T>(fileId);
        _dataManager.CleanupDataFile(fileId);
        return result;
    }

    public void StopBackgroundProcess()
    {
        try
        {
            if (_backgroundProcess != null && !_backgroundProcess.HasExited)
            {
                _backgroundProcess.Kill();
                _backgroundProcess.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping background process: {ex.Message}");
        }
    }
}