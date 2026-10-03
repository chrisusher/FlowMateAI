using Shared.Contracts;
using Shared.Enums;

namespace Services.Workspaces;

public sealed record WorkspaceSaveResult(WorkspaceResponse? Response, WorkspaceSaveErrorCode? ErrorCode, string? ErrorMessage)
{
    public bool Succeeded => Response is not null;
}
