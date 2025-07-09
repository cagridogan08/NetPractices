namespace ProjectExporter.Models;

public class ProjectStatistics
{
    public int TotalProjects { get; set; }
    public int UniqueProjects { get; set; }
    public long TotalSizeBytes { get; set; }
    public string FormattedTotalSize { get; set; }
    public int TotalFiles { get; set; }
    public double AverageProjectSize { get; set; }
    public string LatestProject { get; set; }
    public string OldestProject { get; set; }
}