using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class BillingRepositoryTests
{
    [Test]
    public async Task WebhookUpdateDoesNotCreateAMissingEntitlement()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var database = new DatabaseContext(options);
        var repository = new BillingRepository(database);

        var updated = await repository.UpdateExistingEntitlementAsync("user", entitlement => entitlement.StripeSubscriptionId = "sub_new");

        Assert.That(updated, Is.False);
        Assert.That(await database.BillingEntitlements.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task WebhookUpdateCannotRecreateAnEntitlementDeletedDuringTheUpdate()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options;
        await using var database = new DatabaseContext(options);
        await using var cleanup = new DatabaseContext(options);
        database.BillingEntitlements.Add(new BillingEntitlementDocument { UserId = "user" });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var repository = new BillingRepository(database);

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(async () => await repository.UpdateExistingEntitlementAsync("user", entitlement =>
        {
            var deleted = cleanup.BillingEntitlements.Single();
            cleanup.BillingEntitlements.Remove(deleted);
            cleanup.SaveChanges();
            entitlement.StripeSubscriptionId = "sub_new";
        }));

        Assert.That(await cleanup.BillingEntitlements.CountAsync(), Is.Zero);
    }
}
