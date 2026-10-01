using Shared.Contracts;

namespace Services.Workspaces;

public interface IWorkspaceService
{
    Task<WorkspaceResponse> GetAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<WorkspaceSaveResult> SaveAsync(string ownerId, WorkspaceSaveRequest request, CancellationToken cancellationToken = default);
}
