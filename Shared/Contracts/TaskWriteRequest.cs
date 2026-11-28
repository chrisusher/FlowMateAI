using Shared.Models;

namespace Shared.Contracts;

public sealed record TaskWriteRequest(TaskRecord? Task, string? Revision, string? WorkspaceRevision = null);
