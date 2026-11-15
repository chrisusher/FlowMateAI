namespace Services.Repositories;

public interface IAccountDeletionMarkerRepository
{
    Task MarkAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> IsMarkedAsync(string userId, CancellationToken cancellationToken = default);
}
