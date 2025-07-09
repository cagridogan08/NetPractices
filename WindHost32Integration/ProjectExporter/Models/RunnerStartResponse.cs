namespace ProjectExporter;

public class RunnerStartResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public int? ProcessId { get; set; }
    public string ProjectName { get; set; }
    public string RunnerPath { get; set; }
}