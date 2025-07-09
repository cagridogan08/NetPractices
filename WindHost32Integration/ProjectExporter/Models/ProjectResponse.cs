namespace ProjectExporter.Models;

public class ProjectResponse
{
    public List<ProjectInfo> Projects { get; set; } = new();
    public int Count { get; set; }
    public bool GroupedByProject { get; set; }
    public DateTime Timestamp { get; set; }
}