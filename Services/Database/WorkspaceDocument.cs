using Shared.Models;

namespace Services.Database;

public sealed class WorkspaceDocument
{
    public string Id
    {
        get; set;
    } = "workspace";
    public string UserId
    {
        get; set;
    } = "";
    // Legacy documents only. New saves clear this after moving the data to Workspace.
    public string? Payload
    {
        get; set;
    }
    public WorkspaceSnapshot? Workspace
    {
        get; set;
    }
    // Set only after project documents have been copied and verified during cutover.
    public bool ProjectsAuthoritative { get; set; }
    // Set only after task documents have been copied and verified during cutover.
    public bool TasksAuthoritative { get; set; }
    public string ProjectIndexId { get; set; } = "project-index";
    public ProjectArchiveOperation? PendingProjectArchive { get; set; }
    public TaskGuardOperation? PendingTaskOperation { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ETag
    {
        get; set;
    }
}
