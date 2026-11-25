namespace Services.Repositories;

public interface IProjectArchiveRepository
{
    Task<ProjectWriteResult> ArchiveAsync(string userId, string id, string projectRevision, string workspaceRevision, CancellationToken cancellationToken = default);
    Task<ProjectWriteResult?> RecoverAsync(string userId, CancellationToken cancellationToken = default);
}
