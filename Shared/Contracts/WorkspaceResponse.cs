using Shared.Models;

namespace Shared.Contracts;

public sealed record WorkspaceResponse(WorkspaceSnapshot Workspace, string Revision);
