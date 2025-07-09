using System.IO.Pipes;
using System.Text.Json;
using Models;
using Models.SharedLibrary.Models;

public class ProcessingServer
{
    private readonly string _pipeName;
    private readonly DataTransferManager _dataManager;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public ProcessingServer(string pipeName = "MyAppDataPipe")
    {
        _pipeName = pipeName;
        _dataManager = new DataTransferManager(pipeName);
    }

    public async Task StartAsync()
    {
        Console.WriteLine($"Starting processing server on pipe: {_pipeName}");

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(_pipeName,
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte);

                Console.WriteLine("Waiting for client connection...");
                await server.WaitForConnectionAsync(_cancellationTokenSource.Token);
                Console.WriteLine("Client connected");

                await HandleClientAsync(server);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Server error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        try
        {
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe);

            var requestJson = await reader.ReadLineAsync();
            var request = JsonSerializer.Deserialize<ProcessingRequest>(requestJson);

            Console.WriteLine($"Processing request: {request.Operation}");

            var response = await ProcessRequestAsync(request);

            var responseJson = JsonSerializer.Serialize(response);
            await writer.WriteLineAsync(responseJson);
            await writer.FlushAsync();

            Console.WriteLine($"Request {request.Id} completed: {response.Success}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
        }
    }

    private async Task<ProcessingResponse> ProcessRequestAsync(ProcessingRequest request)
    {
        try
        {
            switch (request.Operation.ToLower())
            {
                case "import":
                    return await ImportDataAsync(request);
                case "export":
                    return await ExportDataAsync(request);
                case "process_large_dataset":
                    return await ProcessLargeDatasetAsync(request);
                default:
                    return new ProcessingResponse
                    {
                        RequestId = request.Id,
                        Success = false,
                        Message = $"Unknown operation: {request.Operation}"
                    };
            }
        }
        catch (Exception ex)
        {
            return new ProcessingResponse
            {
                RequestId = request.Id,
                Success = false,
                Message = ex.Message
            };
        }
    }

    private async Task<ProcessingResponse> ImportDataAsync(ProcessingRequest request)
    {
        // Simulate data import
        await Task.Delay(2000);

        var sampleData = Enumerable.Range(1, 100).Select(i => new DataRecord
        {
            Id = i,
            Name = $"Record {i}",
            Value = i * 10.5m,
            CreatedAt = DateTime.Now.AddMinutes(-i)
        }).ToList();

        // For large datasets, save to file and return file ID
        var fileId = await _dataManager.WriteDataFileAsync(sampleData);

        return new ProcessingResponse
        {
            RequestId = request.Id,
            Success = true,
            Message = "Data imported successfully",
            Data = new { FileId = fileId, RecordCount = sampleData.Count }
        };
    }

    private async Task<ProcessingResponse> ExportDataAsync(ProcessingRequest request)
    {
        // Get file ID from parameters
        if (!request.Parameters.TryGetValue("fileId", out var fileIdObj))
        {
            return new ProcessingResponse
            {
                RequestId = request.Id,
                Success = false,
                Message = "FileId parameter required"
            };
        }

        var fileId = fileIdObj.ToString();

        try
        {
            var data = await _dataManager.ReadDataFileAsync<List<DataRecord>>(fileId);

            // Simulate export processing
            await Task.Delay(1500);

            var exportPath = Path.Combine(Path.GetTempPath(), $"export_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            // Simple CSV export
            var csvLines = new List<string> { "Id,Name,Value,CreatedAt" };
            csvLines.AddRange(data.Select(r => $"{r.Id},{r.Name},{r.Value},{r.CreatedAt:yyyy-MM-dd HH:mm:ss}"));

            await File.WriteAllLinesAsync(exportPath, csvLines);

            // Cleanup transfer file
            _dataManager.CleanupDataFile(fileId);

            return new ProcessingResponse
            {
                RequestId = request.Id,
                Success = true,
                Message = "Data exported successfully",
                Data = new { ExportPath = exportPath, RecordCount = data.Count }
            };
        }
        catch (FileNotFoundException)
        {
            return new ProcessingResponse
            {
                RequestId = request.Id,
                Success = false,
                Message = "Transfer file not found"
            };
        }
    }

    private async Task<ProcessingResponse> ProcessLargeDatasetAsync(ProcessingRequest request)
    {
        // Simulate heavy processing with progress updates
        var totalSteps = 10;
        for (int i = 0; i < totalSteps; i++)
        {
            await Task.Delay(500); // Simulate work
            var progress = (i + 1) * 100 / totalSteps;
            Console.WriteLine($"Progress: {progress}%");
        }

        return new ProcessingResponse
        {
            RequestId = request.Id,
            Success = true,
            Message = "Large dataset processed successfully",
            Progress = 100
        };
    }

    public void Stop()
    {
        _cancellationTokenSource.Cancel();
    }
}