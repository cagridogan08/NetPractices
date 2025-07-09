namespace ProjectExporter.Models;

public class AppSettings
{
    public string ApiBaseUrl { get; set; } = "https://localhost:7001/api";
    public List<string> RecentServers { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public bool AutoConnect { get; set; } = true;
    public int ConnectionTimeout { get; set; } = 30;
}