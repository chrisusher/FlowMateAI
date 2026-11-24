using Services.Repositories;
using Shared.Models;

namespace Services.Workspaces;

public interface IProjectWriteService
{
    Task<ProjectWriteResult> CreateAsync(string userId, ProjectRecord project, CancellationToken cancellationToken = default);
    Task<ProjectWriteResult> RenameAsync(string userId, string id, string? name, string revision, CancellationToken cancellationToken = default);
}
