namespace Services.Repositories;

public interface IUserDataResetRepository
{
    Task<int> DeleteWorkspaceDocumentsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteWorkspaceRecordsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteBillingEntitlementsAsync(string userId, CancellationToken cancellationToken = default);
    Task<int> DeleteMcpUsageAsync(string userId, CancellationToken cancellationToken = default);
}
