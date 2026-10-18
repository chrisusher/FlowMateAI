using Services.Database;
using Services.Repositories;

namespace Services.Tests.Doubles;

public class StubBillingRepository : IBillingRepository
{
    private readonly SemaphoreSlim _promptGate = new(1, 1);
    
    public List<BillingEntitlementDocument> SavedEntitlements { get; } = [];

    public BillingEntitlementDocument Entitlement
    {
        get; init;
    } = new()
    {
        UserId = "user"
    };

    public async Task<(bool Allowed, int Used, int Limit)> TryConsumePromptAsync(
        string userId, string month, int freeLimit, int proLimit, CancellationToken cancellationToken = default)
    {
        await _promptGate.WaitAsync(cancellationToken);

        try
        {
            var used = string.Equals(Entitlement.PromptMonth, month, StringComparison.Ordinal) ? Entitlement.PromptCount : 0;
            var limit = string.Equals(Entitlement.Plan, "Pro", StringComparison.Ordinal) ? proLimit : freeLimit;

            if (used >= limit)
            {
                return (false, used, limit);
            }

            Entitlement.PromptMonth = month;
            Entitlement.PromptCount = used + 1;

            return (true, Entitlement.PromptCount, limit);
        }
        finally
        {
            _promptGate.Release();
        }
    }

    public async Task ReleasePromptAsync(string userId, string month, CancellationToken cancellationToken = default)
    {
        await _promptGate.WaitAsync(cancellationToken);

        try
        {
            if (string.Equals(Entitlement.PromptMonth, month, StringComparison.Ordinal) && Entitlement.PromptCount > 0)
            {
                Entitlement.PromptCount--;
            }
        }
        finally
        {
            _promptGate.Release();
        }
    }

    public Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(Entitlement);

    public Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default)
    {
        SavedEntitlements.Add(JsonSerializer.Deserialize<BillingEntitlementDocument>(JsonSerializer.Serialize(entitlement))!);
        Entitlement.StripeCustomerId = entitlement.StripeCustomerId;

        return Task.CompletedTask;
    }

    public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
