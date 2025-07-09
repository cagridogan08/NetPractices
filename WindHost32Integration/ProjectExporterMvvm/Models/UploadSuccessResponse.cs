namespace ProjectExporterMvvm.Models;

public class UploadSuccessResponse
{
    public string Message { get; set; }
    public string ProjectName { get; set; }
    public string Version { get; set; }
    public bool WasOverridden { get; set; }
    public string FileName { get; set; }
    public long Size { get; set; }
    public DateTime Timestamp { get; set; }
}