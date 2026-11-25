using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace Services.Repositories;

public sealed class WorkspaceReportRepository(DatabaseContext database) : IWorkspaceReportRepository
{
    public async Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default)
    {
        var document = await database.Workspaces.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            return "missing";
        }

        var projects = document.ProjectsAuthoritative ? await ReadProjectsAsync(userId, cancellationToken) : null;

        return Revision(document, projects);
    }

    public async Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var document = await database.Workspaces.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == userId, cancellationToken);

        if (document is null)
        {
            return new(new WorkspaceSnapshot(), "missing");
        }

        var workspace = WorkspaceStorageFormat.Read(document);
        var projects = document.ProjectsAuthoritative ? await ReadProjectsAsync(userId, cancellationToken) : null;

        if (projects is not null)
        {
            workspace.Projects = projects;
        }

        return new(workspace, Revision(document, projects));
    }

    public async Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        var document = await database.BillingEntitlements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "billing" && x.UserId == userId, cancellationToken);

        return document is null ? null : new(BillingPlanExtensions.ParseOrFree(document.Plan), SubscriptionEntitlementExtensions.FromProviderStatus(document.SubscriptionStatus));
    }

    private async Task<List<ProjectRecord>> ReadProjectsAsync(string userId, CancellationToken cancellationToken)
    {
        var documents = await database.Projects.AsNoTracking().Where(item => item.UserId == userId)
            .OrderBy(item => item.Position).ThenBy(item => item.Id).ToListAsync(cancellationToken);

        return documents.Select(item => item.Project ?? throw new InvalidDataException("A project document has no project data.")).ToList();
    }

    private static string Revision(WorkspaceDocument document, List<ProjectRecord>? projects)
    {
        var revision = document.ETag ?? document.UpdatedAt.UtcTicks.ToString();

        return projects is null ? revision : $"{revision}:{WorkspaceCollectionManifest.Hash(projects)}";
    }
}
