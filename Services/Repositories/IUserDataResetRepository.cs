namespace Services.Repositories;

public interface IUserDataResetRepository
{
    Task<int> DeleteWorkspaceDocumentsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteWorkspaceRecordsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteWorkspaceProjectsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteWorkspaceTasksAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteWorkspaceSessionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteWorkspaceConversationsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteBillingEntitlementsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteUserRecordsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteMcpUsageAsync(string userId, CancellationToken cancellationToken = default);
}
