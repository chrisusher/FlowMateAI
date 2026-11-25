using Services.Repositories;
using Shared.Models;

namespace Services.Workspaces;

public interface IProjectWriteService
{
    Task<ProjectWriteResult> CreateAsync(string userId, ProjectRecord project, CancellationToken cancellationToken = default);
    Task<ProjectWriteResult> RenameAsync(string userId, string id, string? name, string revision, CancellationToken cancellationToken = default);
    Task<ProjectWriteResult> RestoreAsync(string userId, string id, string revision, CancellationToken cancellationToken = default);
    Task<ProjectWriteResult> ArchiveAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default);
}
