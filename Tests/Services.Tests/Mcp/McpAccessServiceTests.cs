using Services.Mcp;
using Services.Repositories;
using Shared.Enums;

namespace Services.Tests.Mcp;

public sealed class McpAccessServiceTests
{
    [Test]
    public async Task OnlyActiveProAndUnexpiredProTrialsCanUseAssistant()
    {
        var repository = new BillingRepository();
        var service = new McpAccessService(repository, TimeProvider.System);

        Assert.That(await service.IsAllowedAsync("user"), Is.False);
        repository.Access = new(BillingPlan.Free, SubscriptionEntitlement.NotEntitled);
        Assert.That(await service.IsAllowedAsync("user"), Is.False);
        repository.Access = new(BillingPlan.Pro, SubscriptionEntitlement.Entitled, SubscriptionStatus.Active);
        Assert.That(await service.IsAllowedAsync("user"), Is.True);
        repository.Access = new(BillingPlan.Pro, SubscriptionEntitlement.Entitled, SubscriptionStatus.Trialing, DateTimeOffset.UtcNow.AddDays(1));
        Assert.That(await service.IsAllowedAsync("user"), Is.True);
        repository.Access = repository.Access with { PeriodEndsAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
        Assert.That(await service.IsAllowedAsync("user"), Is.False);
        repository.Access = repository.Access with { PeriodEndsAt = null };
        Assert.That(await service.IsAllowedAsync("user"), Is.False);
        repository.Access = new(BillingPlan.Pro, SubscriptionEntitlement.NotEntitled, SubscriptionStatus.Canceled);
        Assert.That(await service.IsAllowedAsync("user"), Is.False);
    }

    private sealed class BillingRepository : IWorkspaceReportRepository
    {
        public BillingAccess? Access { get; set; }
        public Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(Access);
        public Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
