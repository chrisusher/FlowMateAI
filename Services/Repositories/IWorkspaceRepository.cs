using Services.Database;
using Shared.Models;

namespace Services.Repositories;

public interface IWorkspaceRepository
{
    Task<WorkspaceDocument?> GetAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<WorkspaceDocument?> SaveAsync(string ownerId, WorkspaceSnapshot workspace, string? expectedRevision, CancellationToken cancellationToken = default);
}
