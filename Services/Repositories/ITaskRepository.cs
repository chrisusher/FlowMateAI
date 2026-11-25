using Shared.Models;

namespace Services.Repositories;

public interface ITaskRepository
{
    Task<TaskWriteResult?> RecoverAsync(string userId, CancellationToken cancellationToken = default);
    Task<TaskWriteResult> GetAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<TaskWriteResult> CreateAsync(string userId, TaskRecord task, string workspaceRevision, CancellationToken cancellationToken = default);
    Task<TaskWriteResult> UpdateAsync(string userId, TaskRecord task, string revision, string workspaceRevision, CancellationToken cancellationToken = default);
    Task<TaskWriteResult> DeleteAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default);
}
