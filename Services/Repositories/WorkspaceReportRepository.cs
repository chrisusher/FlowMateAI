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

        var tasks = document.TasksAuthoritative ? await ReadTasksAsync(userId, cancellationToken) : null;

        return Revision(document, projects, tasks);
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

        var tasks = document.TasksAuthoritative ? await ReadTasksAsync(userId, cancellationToken) : null;

        if (tasks is not null)
        {
            workspace.Tasks = tasks;
        }

        return new(workspace, Revision(document, projects, tasks));
    }

    public async Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        var document = await database.BillingEntitlements.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "billing" && x.UserId == userId, cancellationToken);

        return document is null ? null : new(BillingPlanExtensions.ParseOrFree(document.Plan), SubscriptionEntitlementExtensions.FromProviderStatus(document.SubscriptionStatus));
    }

    private async Task<List<ProjectRecord>> ReadProjectsAsync(string userId, CancellationToken cancellationToken)
    {
        var documents = await database.Projects.AsNoTracking().Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);

        return documents.OrderBy(item => item.Position).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Project ?? throw new InvalidDataException("A project document has no project data.")).ToList();
    }

    private async Task<List<TaskRecord>> ReadTasksAsync(string userId, CancellationToken cancellationToken)
    {
        var documents = await database.Tasks.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(cancellationToken);

        return documents.Where(item => !item.IsDeleted).OrderBy(item => item.Position).ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => item.Task ?? throw new InvalidDataException("A task document has no task data.")).ToList();
    }

    private static string Revision(WorkspaceDocument document, List<ProjectRecord>? projects, List<TaskRecord>? tasks)
    {
        var revision = document.ETag ?? document.UpdatedAt.UtcTicks.ToString();

        if (projects is not null)
        {
            revision += $":{WorkspaceCollectionManifest.Hash(projects)}";
        }

        return tasks is null ? revision : $"{revision}:{WorkspaceCollectionManifest.Hash(tasks)}";
    }
}
