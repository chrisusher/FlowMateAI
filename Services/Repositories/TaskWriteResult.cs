using Shared.Enums;
using Shared.Models;

namespace Services.Repositories;

public sealed record TaskWriteResult(TaskRecord? Task, string? Revision, TaskChangeRejection Rejection,
    string? WorkspaceRevision = null, TimerSnapshot? Timer = null);
