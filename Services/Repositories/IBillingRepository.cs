using Services.Database;

namespace Services.Repositories;

public interface IBillingRepository
{
    Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default);
    Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default);
    Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default);
    Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default);
}
