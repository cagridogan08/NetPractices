namespace ProjectExporterMvvm.Models;

public class ProjectInfo
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public DateTime LastAccessedDate { get; set; }
    public long SizeInBytes { get; set; }
    public string FormattedSize { get; set; }
    public int FileCount { get; set; }
    public string Status { get; set; }
    public string Version { get; set; } = "1.0.0";
    public int VersionNumber { get; set; } = 1;
    public string BaseProjectName { get; set; }
    public List<string> AvailableVersions { get; set; } = new();
    public ProjectMetadata Metadata { get; set; } = new();
}