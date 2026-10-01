using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

public sealed class BillingRepository(DatabaseContext database) : IBillingRepository
{
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private static bool _initialized;

    public async Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        return await database.BillingEntitlements.FirstOrDefaultAsync(x => x.Id == "billing" && x.UserId == userId, cancellationToken)
            ?? new BillingEntitlementDocument { UserId = userId };
    }

    public async Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var existing = await database.BillingEntitlements.FirstOrDefaultAsync(x => x.Id == "billing" && x.UserId == entitlement.UserId, cancellationToken);

        if (existing is null)
        {
            database.BillingEntitlements.Add(entitlement);
        }
        else
        {
            existing.StripeCustomerId = entitlement.StripeCustomerId;
            existing.StripeSubscriptionId = entitlement.StripeSubscriptionId;
            existing.Plan = entitlement.Plan;
            existing.SubscriptionStatus = entitlement.SubscriptionStatus;
            existing.TrialUsed = entitlement.TrialUsed;
            existing.PeriodEndsAt = entitlement.PeriodEndsAt;
            existing.PromptMonth = entitlement.PromptMonth;
            existing.PromptCount = entitlement.PromptCount;
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        return await database.StripeEvents.AnyAsync(x => x.Id == eventId && x.PartitionId == "stripe", cancellationToken);
    }

    public async Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        database.StripeEvents.Add(new StripeEventDocument { Id = eventId });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) { /* A concurrent webhook already recorded the same event id. */ }
    }

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await InitializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialized)
            {
                return;
            }

            await database.Database.EnsureCreatedAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            InitializationLock.Release();
        }
    }
}
