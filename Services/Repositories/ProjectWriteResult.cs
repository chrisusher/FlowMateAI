using Shared.Enums;
using Shared.Models;

namespace Services.Repositories;

public sealed record ProjectWriteResult(ProjectRecord? Project, string? Revision, ProjectChangeRejection Rejection,
    string? WorkspaceRevision = null, TimerSnapshot? Timer = null);
