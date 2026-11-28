using Shared.Enums;
using Shared.Models;

namespace Services.Database;

/// <summary>Records a task operation while its project and timer references are guarded.</summary>
public sealed class TaskGuardOperation
{
    public string OperationId { get; set; } = "";
    public TaskGuardOperationKind Kind { get; set; }
    public string TaskId { get; set; } = "";
    public string? OriginRevision { get; set; }
    public TaskRecord? Task { get; set; }
}
