using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Services.Database;
using Services.Mcp;
using Services.Repositories;

namespace Services.Tests;

[TestFixture]
public sealed class FreeUsageLimiterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task FirstAdmissionCreatesUsageWithConfiguredLimits()
    {
        var repository = new StubRepository();
        var admission = await CreateLimiter(repository).AdmitAsync("user");

        Assert.That(admission.Admitted, Is.True);
        Assert.That(admission.MinuteRemaining, Is.EqualTo(1));
        Assert.That(admission.DayRemaining, Is.EqualTo(2));
        Assert.That(admission.MinuteResetAt, Is.EqualTo(Now.AddMinutes(1)));
        Assert.That(admission.DayResetAt.UtcDateTime, Is.EqualTo(Now.Date.AddDays(1)));
        Assert.That(repository.Saved!.Id, Is.EqualTo("usage"));
        Assert.That(repository.Saved.UserId, Is.EqualTo("user"));
        Assert.That(repository.Saved.UtcDay, Is.EqualTo("2026-10-03"));
        Assert.That(repository.Saved.DailyCount, Is.EqualTo(1));
        Assert.That(repository.Saved.RecentAdmissions, Is.EqualTo(new[] { Now }));
        Assert.That(repository.Saved.Ttl, Is.EqualTo(172800));
        Assert.That(repository.ExpectedRevision, Is.Null);
    }

    [Test]
    public async Task PreviousDayAndExpiredMinuteAdmissionsReset()
    {
        var repository = new StubRepository
        {
            Document = new McpUsageDocument
            {
                UtcDay = "2026-10-02",
                DailyCount = 3,
                RecentAdmissions = [Now.AddMinutes(-2), Now.AddMinutes(-1)],
                ETag = "revision"
            }
        };
        var admission = await CreateLimiter(repository).AdmitAsync("user");

        Assert.That(admission.Admitted, Is.True);
        Assert.That(repository.Saved!.DailyCount, Is.EqualTo(1));
        Assert.That(repository.Saved.RecentAdmissions, Is.EqualTo(new[] { Now }));
        Assert.That(repository.ExpectedRevision, Is.EqualTo("revision"));
    }

    [TestCase(2, 2, 50)]
    [TestCase(0, 3, 43200)]
    [TestCase(2, 3, 43200)]
    public async Task ExhaustedQuotaRejectsWithoutWriting(int recentCount, int dailyCount, int retryAfter)
    {
        var repository = new StubRepository
        {
            Document = new McpUsageDocument
            {
                UtcDay = "2026-10-03",
                DailyCount = dailyCount,
                RecentAdmissions = Enumerable.Repeat(Now.AddSeconds(-10), recentCount).ToList()
            }
        };
        var admission = await CreateLimiter(repository).AdmitAsync("user");

        Assert.That(admission.Admitted, Is.False);
        Assert.That(admission.RetryAfterSeconds, Is.EqualTo(retryAfter));
        Assert.That(repository.Writes, Is.Zero);
    }

    [Test]
    public async Task ConcurrentAdmissionIsRereadAndCannotExceedQuota()
    {
        var repository = new StubRepository();
        repository.Save = (_, _) =>
        {
            repository.Document = new McpUsageDocument
            {
                UtcDay = "2026-10-03",
                DailyCount = 2,
                RecentAdmissions = [Now, Now],
                ETag = "winner"
            };

            return false;
        };
        var admission = await CreateLimiter(repository).AdmitAsync("user");

        Assert.That(admission.Admitted, Is.False);
        Assert.That(repository.Reads, Is.EqualTo(2));
        Assert.That(repository.Writes, Is.EqualTo(1));
    }

    [Test]
    public void RepeatedConflictsStopAfterFiveAttempts()
    {
        var repository = new StubRepository { Save = (_, _) => false };

        Assert.ThrowsAsync<UsageAdmissionUnavailableException>(() => CreateLimiter(repository).AdmitAsync("user"));
        Assert.That(repository.Reads, Is.EqualTo(5));
        Assert.That(repository.Writes, Is.EqualTo(5));
    }

    [Test]
    public void StorageFailureBecomesAdmissionUnavailable()
    {
        var failure = new McpUsageStorageUnavailableException(new InvalidOperationException("unavailable"));
        var repository = new StubRepository { Read = _ => throw failure };
        var exception = Assert.ThrowsAsync<UsageAdmissionUnavailableException>(() => CreateLimiter(repository).AdmitAsync("user"));

        Assert.That(exception!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public void CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var repository = new StubRepository();

        Assert.CatchAsync<OperationCanceledException>(() => CreateLimiter(repository).AdmitAsync("user", cancellation.Token));
        Assert.That(repository.Writes, Is.Zero);
    }

    [Test]
    public void StorageTimeoutBecomesAdmissionUnavailable()
    {
        var repository = new StubRepository
        {
            Read = async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);

                return null;
            }
        };

        Assert.ThrowsAsync<UsageAdmissionUnavailableException>(() => CreateLimiter(repository).AdmitAsync("user"));
        Assert.That(repository.Writes, Is.Zero);
    }

    private static FreeUsageLimiterService CreateLimiter(IMcpUsageRepository repository)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mcp:FreePerMinute"] = "2",
            ["Mcp:FreePerDay"] = "3"
        }).Build();

        return new FreeUsageLimiterService(repository, configuration, new FixedClock());
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubRepository : IMcpUsageRepository
    {
        public McpUsageDocument? Document { get; set; }
        public McpUsageDocument? Saved { get; private set; }
        public string? ExpectedRevision { get; private set; }
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Func<CancellationToken, Task<McpUsageDocument?>>? Read { get; init; }
        public Func<McpUsageDocument, string?, bool>? Save { get; set; }

        public Task<McpUsageDocument?> GetAsync(string userId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;

            return Read?.Invoke(cancellationToken) ?? Task.FromResult(Document);
        }

        public Task<bool> TrySaveAsync(McpUsageDocument document, string? expectedRevision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes++;
            Saved = document;
            ExpectedRevision = expectedRevision;

            return Task.FromResult(Save?.Invoke(document, expectedRevision) ?? true);
        }
    }
}
