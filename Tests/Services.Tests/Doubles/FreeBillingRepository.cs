using Services.Database;
using Services.Repositories;

namespace Services.Tests.Doubles;

public class FreeBillingRepository : IBillingRepository
{
    public Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingEntitlementDocument
            {
                UserId = userId,
                Plan = BillingPlan.Free.ToString()
            });

    public Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> TrySaveEntitlementAsync(BillingEntitlementDocument entitlement, string? expectedRevision, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
