using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;
using Services.Database;
using Services.Repositories;

namespace Services.Tests;

[TestFixture]
public sealed class McpUsageRepositoryTests
{
    [Test]
    public void ModelPreservesExistingUsageSchemaAndConcurrency()
    {
        using var database = new RecordingContext();
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(McpUsageDocument))!;

        Assert.That(entity.GetContainer(), Is.EqualTo("McpUsage"));
        Assert.That(entity.FindPrimaryKey()!.Properties.Select(x => x.Name), Is.EqualTo(new[] { "Id", "UserId" }));
        Assert.That(entity.GetPartitionKeyPropertyNames(), Is.EqualTo(new[] { "UserId" }));
        Assert.That(entity.FindDiscriminatorProperty(), Is.Null);
        Assert.That(entity.FindProperty(nameof(McpUsageDocument.Id))!.GetJsonPropertyName(), Is.EqualTo("id"));
        Assert.That(entity.FindProperty(nameof(McpUsageDocument.UserId))!.GetJsonPropertyName(), Is.EqualTo("userId"));
        Assert.That(entity.FindProperty(nameof(McpUsageDocument.UtcDay))!.GetJsonPropertyName(), Is.EqualTo("utcDay"));
        Assert.That(entity.FindProperty(nameof(McpUsageDocument.DailyCount))!.GetJsonPropertyName(), Is.EqualTo("dailyCount"));
        Assert.That(entity.FindProperty(nameof(McpUsageDocument.RecentAdmissions))!.GetJsonPropertyName(), Is.EqualTo("recentAdmissions"));
        Assert.That(entity.FindProperty(nameof(McpUsageDocument.Ttl))!.GetJsonPropertyName(), Is.EqualTo("ttl"));
        Assert.That(entity.GetDefaultTimeToLive(), Is.EqualTo(172800));
        var etag = entity.FindProperty(nameof(McpUsageDocument.ETag))!;
        Assert.That(etag.IsConcurrencyToken, Is.True);
        Assert.That(etag.GetJsonPropertyName(), Is.EqualTo("_etag"));
    }

    [TestCase(null, EntityState.Added)]
    [TestCase("revision", EntityState.Modified)]
    public async Task SaveUsesExpectedRevisionAndDetaches(string? revision, EntityState expectedState)
    {
        using var database = new RecordingContext();
        var document = new McpUsageDocument { UserId = "user", DailyCount = 1 };

        Assert.That(await new McpUsageRepository(database).TrySaveAsync(document, revision), Is.True);
        Assert.That(database.SavedState, Is.EqualTo(expectedState));
        Assert.That(database.OriginalRevision, Is.EqualTo(revision));
        Assert.That(database.ChangeTracker.Entries<McpUsageDocument>(), Is.Empty);
    }

    [TestCase(HttpStatusCode.Conflict, null)]
    [TestCase(HttpStatusCode.PreconditionFailed, "stale")]
    public async Task FailedWriteCanBeRetriedWithAFreshEntity(HttpStatusCode status, string? revision)
    {
        using var database = new RecordingContext
        {
            Failure = new DbUpdateException("conflict", new CosmosException("conflict", status, 0, "activity", 0))
        };
        var repository = new McpUsageRepository(database);

        Assert.That(await repository.TrySaveAsync(new McpUsageDocument { UserId = "user" }, revision), Is.False);
        Assert.That(database.ChangeTracker.Entries<McpUsageDocument>(), Is.Empty);
        database.Failure = null;
        Assert.That(await repository.TrySaveAsync(new McpUsageDocument { UserId = "user" }, "fresh"), Is.True);
        Assert.That(database.OriginalRevision, Is.EqualTo("fresh"));
    }

    [Test]
    public async Task EfConcurrencyFailureReturnsFalseAndDetaches()
    {
        using var database = new RecordingContext { Failure = new DbUpdateConcurrencyException() };

        Assert.That(await new McpUsageRepository(database).TrySaveAsync(new McpUsageDocument { UserId = "user" }, "stale"), Is.False);
        Assert.That(database.ChangeTracker.Entries<McpUsageDocument>(), Is.Empty);
    }

    [Test]
    public void OtherStorageFailuresDoNotCountAsConflicts()
    {
        using var database = new RecordingContext { Failure = new DbUpdateException("unavailable") };

        Assert.ThrowsAsync<McpUsageStorageUnavailableException>(() =>
            new McpUsageRepository(database).TrySaveAsync(new McpUsageDocument { UserId = "user" }, null));
        Assert.That(database.ChangeTracker.Entries<McpUsageDocument>(), Is.Empty);
    }

    private sealed class RecordingContext() : DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
        .UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "tests").Options)
    {
        public EntityState SavedState { get; private set; }
        public string? OriginalRevision { get; private set; }
        public Exception? Failure { get; set; }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var entry = ChangeTracker.Entries<McpUsageDocument>().Single();
            SavedState = entry.State;
            OriginalRevision = entry.Property(x => x.ETag).OriginalValue;

            if (Failure is not null)
            {
                return Task.FromException<int>(Failure);
            }

            return Task.FromResult(1);
        }
    }
}
