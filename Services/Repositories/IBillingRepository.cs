using Services.Database;

namespace Services.Repositories;

public interface IBillingRepository
{
    Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default);
    Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default);
    // False means the caller must reload both the entitlement and Stripe state before retrying.
    Task<bool> TrySaveEntitlementAsync(BillingEntitlementDocument entitlement, string? expectedRevision, CancellationToken cancellationToken = default);

    Task<(bool Allowed, int Used, int Limit)> TryConsumePromptAsync(
        string userId, string month, int freeLimit, int proLimit, CancellationToken cancellationToken = default) =>
        Task.FromException<(bool Allowed, int Used, int Limit)>(new NotSupportedException("Atomic prompt reservations are not implemented by this repository."));

    Task ReleasePromptAsync(string userId, string month, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Prompt reservation release is not implemented by this repository."));
    Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default);
    Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default);
}
