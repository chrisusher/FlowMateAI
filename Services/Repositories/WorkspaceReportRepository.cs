using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Enums;

namespace Services.Repositories;

public sealed class WorkspaceReportRepository(DatabaseContext database) : IWorkspaceReportRepository
{
    public async Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default)
    {
        var document = await database.Workspaces.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == userId, cancellationToken);

        return document is null ? "missing" : document.ETag ?? document.UpdatedAt.UtcTicks.ToString();
    }

    public async Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var document = await database.Workspaces.AsNoTracking().FirstAsync(x => x.Id == "workspace" && x.UserId == userId, cancellationToken);

        return new(WorkspaceStorageFormat.Read(document), document.ETag ?? document.UpdatedAt.UtcTicks.ToString());
    }

    public async Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        var document = await database.BillingEntitlements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "billing" && x.UserId == userId, cancellationToken);

        return document is null ? null : new(BillingPlanExtensions.ParseOrFree(document.Plan), SubscriptionEntitlementExtensions.FromProviderStatus(document.SubscriptionStatus));
    }
}
