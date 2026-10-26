using Microsoft.Extensions.Configuration;
using Services.ApiRequests;
using Services.Database;
using Services.Repositories;
using Shared.Enums;

namespace Services.Tests.ApiRequests;

[TestFixture]
public sealed class AuthenticatedApiRateLimiterServiceTests
{
    [Test]
    public async Task AdmissionsArePartitionedByUserAndScope()
    {
        var repository = new UsageRepository();
        var limiter = CreateLimiter(repository, limit: 1);

        Assert.That((await limiter.AdmitAsync("user-a", AuthenticatedApiRateLimitScope.CoachRequest)).Admitted, Is.True);
        Assert.That((await limiter.AdmitAsync("user-a", AuthenticatedApiRateLimitScope.CoachRequest)).Admitted, Is.False);
        Assert.That((await limiter.AdmitAsync("user-a", AuthenticatedApiRateLimitScope.BillingRequest)).Admitted, Is.True);
        Assert.That((await limiter.AdmitAsync("user-b", AuthenticatedApiRateLimitScope.CoachRequest)).Admitted, Is.True);
    }

    [Test]
    public async Task AdmissionResetsAfterTheWindow()
    {
        var repository = new UsageRepository();
        var clock = new TestClock(DateTimeOffset.Parse("2026-10-26T12:00:00Z"));
        var limiter = CreateLimiter(repository, limit: 1, clock: clock);

        Assert.That((await limiter.AdmitAsync("user-a", AuthenticatedApiRateLimitScope.WorkspaceWrite)).Admitted, Is.True);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.That((await limiter.AdmitAsync("user-a", AuthenticatedApiRateLimitScope.WorkspaceWrite)).Admitted, Is.True);
    }

    private static AuthenticatedApiRateLimiterService CreateLimiter(UsageRepository repository, int limit, TestClock? clock = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(Enum.GetValues<AuthenticatedApiRateLimitScope>()
                .Select(scope => new KeyValuePair<string, string?>($"ApiLimits:{scope}", limit.ToString())))
            .Build();

        return new(repository, configuration, clock ?? new TestClock(DateTimeOffset.Parse("2026-10-26T12:00:00Z")));
    }

    private sealed class UsageRepository : IMcpUsageRepository
    {
        private readonly Dictionary<string, McpUsageDocument> _documents = new(StringComparer.Ordinal);
        private int _revision;

        public Task<McpUsageDocument?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_documents.TryGetValue(userId, out var document) ? Clone(document) : null);

        public Task<bool> TrySaveAsync(McpUsageDocument document, string? expectedRevision, CancellationToken cancellationToken = default)
        {
            var currentRevision = _documents.GetValueOrDefault(document.UserId)?.ETag;

            if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            document.ETag = $"revision-{++_revision}";
            _documents[document.UserId] = Clone(document);

            return Task.FromResult(true);
        }

        private static McpUsageDocument Clone(McpUsageDocument document) => new()
        {
            UserId = document.UserId,
            UtcDay = document.UtcDay,
            DailyCount = document.DailyCount,
            RecentAdmissions = [.. document.RecentAdmissions],
            ApiAdmissions = [.. document.ApiAdmissions.Select(admission => new ApiRateLimitRecord { Scope = admission.Scope, Timestamp = admission.Timestamp })],
            ETag = document.ETag
        };
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
