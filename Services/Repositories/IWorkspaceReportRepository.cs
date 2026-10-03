using Shared.Models;
using Shared.Enums;

namespace Services.Repositories;

public sealed record WorkspaceReportSource(WorkspaceSnapshot Workspace, string Revision);
public sealed record BillingAccess(BillingPlan Plan, SubscriptionEntitlement Entitlement);

public interface IWorkspaceReportRepository
{
    Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default);
    Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default);
    Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default);
}
