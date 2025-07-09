using System.IO.Pipes;
using System.Text.Json;
using Models.SharedLibrary.Models;

namespace Models;

public class DataTransferManager
{
    private readonly string _pipeName;
    private readonly string _transferDirectory;

    public DataTransferManager(string pipeName = "MyAppDataPipe")
    {
        _pipeName = pipeName;
        _transferDirectory = Path.Combine(Path.GetTempPath(), "MyAppTransfer");
        Directory.CreateDirectory(_transferDirectory);
    }

    // Named Pipe Communication
    public async Task<ProcessingResponse> SendRequestAsync(ProcessingRequest request, int timeoutMs = 30000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            await client.ConnectAsync(timeoutMs);

            using var writer = new StreamWriter(client);
            using var reader = new StreamReader(client);

            var requestJson = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(requestJson);
            await writer.FlushAsync();

            var responseJson = await reader.ReadLineAsync();
            return JsonSerializer.Deserialize<ProcessingResponse>(responseJson);
        }
        catch (Exception ex)
        {
            return new ProcessingResponse
            {
                RequestId = request.Id,
                Success = false,
                Message = $"Communication error: {ex.Message}"
            };
        }
    }

    // File-based data transfer for large datasets
    public async Task<string> WriteDataFileAsync<T>(T data, string fileId = null)
    {
        fileId ??= Guid.NewGuid().ToString();
        var filePath = Path.Combine(_transferDirectory, $"{fileId}.json");

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(filePath, json);
        return fileId;
    }

    public async Task<T> ReadDataFileAsync<T>(string fileId)
    {
        var filePath = Path.Combine(_transferDirectory, $"{fileId}.json");

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Transfer file not found: {fileId}");

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<T>(json);
    }

    public void CleanupDataFile(string fileId)
    {
        var filePath = Path.Combine(_transferDirectory, $"{fileId}.json");
        if (File.Exists(filePath))
            File.Delete(filePath);
    }
}