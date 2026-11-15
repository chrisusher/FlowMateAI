namespace Services.Repositories;

public interface IAccountDeletionMarkerRepository
{
    Task<bool> MarkAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> TryBeginCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default);
    Task EndCheckoutAsync(string userId, string operationId, CancellationToken cancellationToken = default);
}
