using Services.Repositories;
using Shared.Models;

namespace Services.Workspaces;

public interface ITaskService
{
    Task<TaskWriteResult> CreateAsync(string userId, TaskRecord task, string workspaceRevision, CancellationToken cancellationToken = default);
    Task<TaskWriteResult> UpdateAsync(string userId, TaskRecord task, string revision, string workspaceRevision, CancellationToken cancellationToken = default);
    Task<TaskWriteResult> DeleteAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default);
}
