using System.Net;
using Microsoft.Azure.Cosmos;
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

        return await database.BillingEntitlements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "billing" && x.UserId == userId, cancellationToken)
            ?? new BillingEntitlementDocument { UserId = userId };
    }

    public async Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default)
    {
        if (!await TrySaveEntitlementAsync(entitlement, entitlement.ETag, cancellationToken))
        {
            throw new InvalidOperationException("Billing changed during the request. Retry the request.");
        }
    }

    public async Task<bool> TrySaveEntitlementAsync(BillingEntitlementDocument entitlement, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        var entry = database.Entry(entitlement);
        entitlement.ETag = expectedRevision;
        entry.State = expectedRevision is null ? EntityState.Added : EntityState.Modified;
        entry.Property(x => x.ETag).OriginalValue = expectedRevision;
        entry.Property(x => x.ETag).IsModified = false;

        // This is a full document replacement. The ETag protects the prompt counts read with it too.

        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException exception) when (exception.InnerException is CosmosException { StatusCode: HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed })
        {
            return false;
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
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
        var entry = database.StripeEvents.Add(new StripeEventDocument { Id = eventId });

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is CosmosException { StatusCode: HttpStatusCode.Conflict })
        {
            // Only a duplicate insert is safe to acknowledge. Other failures need Stripe to retry.
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // A concurrent delivery already recorded this event.
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
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
