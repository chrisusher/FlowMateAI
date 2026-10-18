using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

public sealed class BillingRepository(DatabaseContext database) : IBillingRepository
{
    private static readonly SemaphoreSlim _initialisationLock = new(1, 1);
    private static bool _initialised;

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
            existing.CancelAtPeriodEnd = entitlement.CancelAtPeriodEnd;
            existing.SubscriptionInterval = entitlement.SubscriptionInterval;
            // Prompt usage is changed only through the atomic reservation/release methods below.
        }
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<(bool Allowed, int Used, int Limit)> TryConsumePromptAsync(
        string userId, string month, int freeLimit, int proLimit, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var entitlement = await database.BillingEntitlements.FirstOrDefaultAsync(
                x => x.Id == "billing" && x.UserId == userId, cancellationToken)
                ?? new BillingEntitlementDocument
                {
                    UserId = userId
                };

            var isNew = database.Entry(entitlement).State == EntityState.Detached;
            var used = string.Equals(entitlement.PromptMonth, month, StringComparison.Ordinal) ? entitlement.PromptCount : 0;
            var limit = BillingPlanExtensions.ParseOrFree(entitlement.Plan) == BillingPlan.Pro ? proLimit : freeLimit;

            if (used >= limit)
            {
                return (false, used, limit);
            }

            entitlement.PromptMonth = month;
            entitlement.PromptCount = used + 1;

            if (isNew)
            {
                database.BillingEntitlements.Add(entitlement);
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return (true, entitlement.PromptCount, limit);
            }
            catch (DbUpdateException) when (attempt < 7)
            {
                database.Entry(entitlement).State = EntityState.Detached;
            }
        }

        throw new DbUpdateException("The prompt allowance changed repeatedly. Retry the request.");
    }

    public async Task ReleasePromptAsync(string userId, string month, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var entitlement = await database.BillingEntitlements.FirstOrDefaultAsync(
                x => x.Id == "billing" && x.UserId == userId, cancellationToken);

            if (entitlement is null
                || !string.Equals(entitlement.PromptMonth, month, StringComparison.Ordinal)
                || entitlement.PromptCount <= 0)
            {
                return;
            }

            entitlement.PromptCount--;

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return;
            }
            catch (DbUpdateException) when (attempt < 7)
            {
                database.Entry(entitlement).State = EntityState.Detached;
            }
        }

        throw new DbUpdateException("The prompt allowance changed repeatedly. Retry the request.");
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
        if (_initialised)
        {
            return;
        }

        await _initialisationLock.WaitAsync(cancellationToken);

        try
        {
            if (_initialised)
            {
                return;
            }

            await database.Database.EnsureCreatedAsync(cancellationToken);
            _initialised = true;
        }
        finally
        {
            _initialisationLock.Release();
        }
    }
}
