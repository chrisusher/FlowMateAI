namespace Services.Database;

/// <summary>Persists enough information to finish an interrupted project archive.</summary>
public sealed class ProjectArchiveOperation
{
    public string ProjectId { get; set; } = "";
    public string ProjectRevision { get; set; } = "";
}
