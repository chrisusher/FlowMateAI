using Shared.Models;

namespace Shared.Contracts;

public sealed record WorkspaceSaveRequest(WorkspaceSnapshot Workspace, string? Revision, bool ReclaimTimer = false);
