using Shared.Models;

namespace Services.Repositories;

public sealed record WorkspaceReportSource(WorkspaceSnapshot Workspace, string Revision);
public sealed record BillingAccess(string Plan, string SubscriptionStatus);

public interface IWorkspaceReportRepository
{
    Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default);
    Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default);
    Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default);
}
