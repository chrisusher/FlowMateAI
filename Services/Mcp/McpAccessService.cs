using Services.Repositories;
using Shared.Enums;

namespace Services.Mcp;

/// <summary>Checks the current account entitlement before issuing or using an assistant key.</summary>
public sealed class McpAccessService(IWorkspaceReportRepository repository, TimeProvider clock)
{
    public async Task<bool> IsAllowedAsync(string userId, CancellationToken cancellationToken = default)
    {
        var access = await repository.GetBillingAccessAsync(userId, cancellationToken);

        return access is { Plan: BillingPlan.Pro, Entitlement: SubscriptionEntitlement.Entitled }
            && (access.Status != SubscriptionStatus.Trialing || access.PeriodEndsAt > clock.GetUtcNow());
    }
}
