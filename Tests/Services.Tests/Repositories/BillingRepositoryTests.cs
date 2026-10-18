using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class BillingRepositoryTests
{
    [TestCase(null, EntityState.Added)]
    [TestCase("revision", EntityState.Modified)]
    public async Task ConditionalSaveUsesReadRevisionAndProtectsPromptUsage(string? revision, EntityState state)
    {
        using var database = new RecordingContext();
        var document = new BillingEntitlementDocument { UserId = "user", PromptCount = 99 };

        Assert.That(await new BillingRepository(database).TrySaveEntitlementAsync(document, revision), Is.True);
        Assert.That(database.SavedState, Is.EqualTo(state));
        Assert.That(database.OriginalRevision, Is.EqualTo(revision));
        Assert.That(database.PromptCountModified, Is.EqualTo(revision is not null));
        Assert.That(database.PromptMonthModified, Is.EqualTo(revision is not null));
        Assert.That(database.ChangeTracker.Entries<BillingEntitlementDocument>(), Is.Empty);
    }

    [TestCase(HttpStatusCode.Conflict)]
    [TestCase(HttpStatusCode.PreconditionFailed)]
    public async Task OnlyConcurrencyFailuresReturnFalse(HttpStatusCode status)
    {
        using var database = new RecordingContext
        {
            Failure = new DbUpdateException("conflict", CosmosFailure(status))
        };

        Assert.That(await new BillingRepository(database).TrySaveEntitlementAsync(new() { UserId = "user" }, "old"), Is.False);
        Assert.That(database.ChangeTracker.Entries<BillingEntitlementDocument>(), Is.Empty);
        database.Failure = new DbUpdateConcurrencyException();
        Assert.That(await new BillingRepository(database).TrySaveEntitlementAsync(new() { UserId = "user" }, "old"), Is.False);
    }

    [Test]
    public void BillingStorageFailurePropagatesAndDetachesFailedWrite()
    {
        using var database = new RecordingContext { Failure = new DbUpdateException("storage unavailable") };

        Assert.ThrowsAsync<DbUpdateException>(() => new BillingRepository(database).TrySaveEntitlementAsync(new() { UserId = "user" }, "read"));
        Assert.That(database.ChangeTracker.Entries<BillingEntitlementDocument>(), Is.Empty);
    }

    [TestCase(HttpStatusCode.ServiceUnavailable)]
    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    public void EventMarkerFailureIsNeverSwallowed(HttpStatusCode status)
    {
        using var database = new RecordingContext { Failure = new DbUpdateException("storage failed", CosmosFailure(status)) };

        Assert.ThrowsAsync<DbUpdateException>(() => new BillingRepository(database).MarkEventProcessedAsync("evt"));
        Assert.That(database.ChangeTracker.Entries<StripeEventDocument>(), Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DuplicateEventInsertIsAcknowledgedAndDetached(bool wrapped)
    {
        using var database = new RecordingContext
        {
            Failure = wrapped ? new DbUpdateException("duplicate", CosmosFailure(HttpStatusCode.Conflict)) : CosmosFailure(HttpStatusCode.Conflict)
        };

        await new BillingRepository(database).MarkEventProcessedAsync("evt");

        Assert.That(database.ChangeTracker.Entries<StripeEventDocument>(), Is.Empty);
    }

    private static CosmosException CosmosFailure(HttpStatusCode status) => new("test failure", status, 0, "test", 0);

    private sealed class RecordingContext() : DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
        .UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "tests").Options)
    {
        public Exception? Failure { get; set; }
        public EntityState SavedState { get; private set; }
        public string? OriginalRevision { get; private set; }
        public bool PromptCountModified { get; private set; }
        public bool PromptMonthModified { get; private set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entry = ChangeTracker.Entries<BillingEntitlementDocument>().SingleOrDefault();

            if (entry is not null)
            {
                SavedState = entry.State;
                OriginalRevision = entry.Property(x => x.ETag).OriginalValue;
                PromptCountModified = entry.Property(x => x.PromptCount).IsModified;
                PromptMonthModified = entry.Property(x => x.PromptMonth).IsModified;
            }

            return Failure is null ? Task.FromResult(1) : Task.FromException<int>(Failure);
        }
    }
}
