namespace ProjectExporterMvvm.Models;

public class VersionCheckResponse
{
    public string ProjectName { get; set; }
    public string Version { get; set; }
    public bool Exists { get; set; }
    public List<string> SuggestedVersions { get; set; } = new();
}