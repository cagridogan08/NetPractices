namespace ProjectExporter;

public class RunnerStatusResponse
{
    public string RunnerPath { get; set; }
    public bool RunnerExists { get; set; }
    public bool IsRunning { get; set; }
    public int ProcessCount { get; set; }
    public RunningProjectInfo[] RunningProjects { get; set; }
    public ProcessInfo[] Processes { get; set; }
    public DateTime Timestamp { get; set; }
}