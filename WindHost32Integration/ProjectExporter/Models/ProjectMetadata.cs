namespace ProjectExporter.Models;

public class ProjectMetadata
{
    public string ProjectName { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime LastModifiedDate { get; set; }
    public string CreatedBy { get; set; }
    public string ModifiedBy { get; set; }
    public Dictionary<string, string> CustomProperties { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string ChangeLog { get; set; }
}