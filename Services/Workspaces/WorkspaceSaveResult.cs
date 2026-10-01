using Shared.Contracts;

namespace Services.Workspaces;

public sealed record WorkspaceSaveResult(WorkspaceResponse? Response, string? ErrorCode, string? ErrorMessage)
{
    public bool Succeeded => Response is not null;
}
