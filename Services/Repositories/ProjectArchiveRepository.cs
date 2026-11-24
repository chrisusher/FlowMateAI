using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Workspaces;
using Shared.Enums;

namespace Services.Repositories;

/// <summary>Coordinates a project archive with the separately stored workspace timer.</summary>
public sealed class ProjectArchiveRepository(DatabaseContext database) : IProjectArchiveRepository
{
    public async Task<ProjectWriteResult> ArchiveAsync(string userId, string id, string projectRevision, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        var pending = await RecoverAsync(userId, cancellationToken);

        if (pending is { Rejection: not ProjectChangeRejection.None })
        {
            return pending;
        }

        database.ChangeTracker.Clear();
        var workspace = await database.Workspaces.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);

        if (workspace is null || !workspace.ProjectsAuthoritative)
        {
            return new(null, null, ProjectChangeRejection.StorageNotReady);
        }

        if (workspace.PendingProjectArchive is not null || string.IsNullOrWhiteSpace(workspaceRevision) || workspace.ETag != workspaceRevision)
        {
            return new(null, null, ProjectChangeRejection.Conflict);
        }

        var project = await database.Projects.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);

        if (project?.Project is null)
        {
            return new(null, null, ProjectChangeRejection.NotFound);
        }

        if (string.IsNullOrWhiteSpace(projectRevision) || project.ETag != projectRevision)
        {
            return new(null, null, ProjectChangeRejection.Conflict);
        }

        var snapshot = WorkspaceStorageFormat.Read(workspace);
        var decision = ProjectChangePolicy.CanArchive(project.Project, snapshot.Timer, snapshot.Tasks);

        if (decision != ProjectChangeRejection.None)
        {
            return new(null, null, decision);
        }

        workspace.PendingProjectArchive = new ProjectArchiveOperation
        {
            ProjectId = id,
            ProjectRevision = projectRevision
        };

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            return new(null, null, ProjectChangeRejection.Conflict);
        }

        return await RecoverAsync(userId, cancellationToken) ?? new(null, null, ProjectChangeRejection.Conflict);
    }

    public async Task<ProjectWriteResult?> RecoverAsync(string userId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            database.ChangeTracker.Clear();
            var workspace = await database.Workspaces.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);
            var operation = workspace?.PendingProjectArchive;

            if (workspace is null || operation is null)
            {
                return null;
            }

            var project = await database.Projects.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == operation.ProjectId, cancellationToken);
            var index = await database.ProjectIndexes.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == workspace.ProjectIndexId, cancellationToken);

            if (index is null)
            {
                return new(null, null, ProjectChangeRejection.StorageNotReady);
            }

            if (project?.Project is null ||
                (!project.Project.IsArchived && project.ETag != operation.ProjectRevision) ||
                (project.Project.IsArchived && project.ArchiveOriginRevision != operation.ProjectRevision))
            {
                workspace.PendingProjectArchive = null;

                try
                {
                    await database.SaveChangesAsync(cancellationToken);

                    return new(null, null, ProjectChangeRejection.Conflict);
                }
                catch (DbUpdateException)
                {
                    continue;
                }
            }

            if (!project.Project.IsArchived)
            {
                if (index.ActiveCount <= 0)
                {
                    return new(null, null, ProjectChangeRejection.StorageNotReady);
                }

                project.Project.IsArchived = true;
                project.ArchiveOriginRevision = operation.ProjectRevision;
                index.ActiveCount--;
                var previous = database.Database.AutoTransactionBehavior;
                database.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;

                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    continue;
                }
                finally
                {
                    database.Database.AutoTransactionBehavior = previous;
                }
            }

            var snapshot = WorkspaceStorageFormat.Read(workspace);
            var timer = snapshot.Timer;
            var referencesProject = timer.ProjectId == project.Id ||
                snapshot.Tasks.Any(task => task.Id == timer.TaskId && task.ProjectId == project.Id);

            if (timer.Phase == TimerPhase.Idle && referencesProject)
            {
                timer.TaskId = null;
                timer.ProjectId = "";
            }

            workspace.Workspace = snapshot;
            workspace.Payload = null;
            workspace.PendingProjectArchive = null;
            workspace.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return new(project.Project, project.ETag, ProjectChangeRejection.None, workspace.ETag, timer);
            }
            catch (DbUpdateException)
            {
                // Another recovery may have finished, or a workspace writer won before the guard.
            }
        }

        return new(null, null, ProjectChangeRejection.Conflict);
    }
}
