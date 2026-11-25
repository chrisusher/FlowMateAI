namespace Services.Database;

/// <summary>Serialises changes to active project slots in one user's Projects partition.</summary>
public sealed class ProjectIndexDocument
{
    public string Id { get; set; } = "project-index";
    public string UserId { get; set; } = "";
    public int ActiveCount { get; set; }
    public int TotalCount { get; set; }
    public int NextPosition { get; set; }
    public string? ETag { get; set; }
}
