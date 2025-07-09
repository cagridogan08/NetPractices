namespace ProjectExporter.Models;

public class VersionConflictResponse
{
    public string Error { get; set; }
    public string Message { get; set; }
    public string ConflictingVersion { get; set; }
    public string ConflictingProjectName { get; set; }
    public ProjectInfo ExistingProject { get; set; }
    public bool RequiresOverrideConfirmation { get; set; }
    public List<string> SuggestedVersions { get; set; } = new();
}