using Shared.Models;

namespace Shared.Contracts;

public sealed record TaskWriteResponse(TaskRecord Task, string Revision, string? WorkspaceRevision = null, TimerSnapshot? Timer = null);
