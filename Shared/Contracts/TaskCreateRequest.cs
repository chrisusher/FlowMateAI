using Shared.Enums;

namespace Shared.Contracts;

public sealed record TaskCreateRequest(TaskCreateData? Task, string? WorkspaceRevision, string? Revision = null);

public sealed record TaskCreateData(string? Id, string? Title, string? ProjectId, DateOnly? DueDate, TaskPriority? Priority,
    bool? IsComplete, bool? PlannedToday, int? FocusMinutesToday, DateTimeOffset? CreatedAt);
