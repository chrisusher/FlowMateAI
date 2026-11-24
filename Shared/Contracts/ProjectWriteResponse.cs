using Shared.Models;

namespace Shared.Contracts;

public sealed record ProjectWriteResponse(ProjectRecord Project, string Revision, string? WorkspaceRevision = null, TimerSnapshot? Timer = null);
