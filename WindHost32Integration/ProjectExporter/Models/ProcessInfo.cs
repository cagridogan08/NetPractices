namespace ProjectExporter;

public class ProcessInfo
{
    public int ProcessId { get; set; }
    public DateTime StartTime { get; set; }
    public long WorkingSet { get; set; }
    public string FormattedMemory { get; set; }
    public bool HasMainWindow { get; set; }
    public string WindowTitle { get; set; }
    public RunningProjectInfo ProjectInfo { get; set; }
}