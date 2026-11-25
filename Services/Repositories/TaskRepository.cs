using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Enums;
using Shared.Models;

namespace Services.Repositories;

/// <summary>Stores tasks as individual documents in the owner's Tasks partition.</summary>
public sealed class TaskRepository(DatabaseContext database) : ITaskRepository
{
    public async Task<TaskWriteResult> GetAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        await RecoverAsync(userId, cancellationToken);
        var workspace = await ReadWorkspaceAsync(userId, cancellationToken);

        if (workspace?.TasksAuthoritative != true || workspace.PendingTaskOperation is not null)
        {
            return new(null, null, TaskChangeRejection.StorageNotReady);
        }

        var document = await database.Tasks.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);

        return document?.Task is null || document.IsDeleted
            ? new(null, null, TaskChangeRejection.NotFound)
            : new(document.Task, document.ETag, TaskChangeRejection.None);
    }

    public async Task<TaskWriteResult> CreateAsync(string userId, TaskRecord task, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        await RecoverAsync(userId, cancellationToken);
        var workspace = await ReadWorkspaceAsync(userId, cancellationToken);

        if (workspace?.TasksAuthoritative != true || workspace.PendingTaskOperation is not null)
        {
            return new(null, null, TaskChangeRejection.StorageNotReady);
        }

        var existing = await database.Tasks.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == task.Id, cancellationToken);

        if (existing is not null)
        {
            return !existing.IsDeleted && existing.Task is not null && Equivalent(existing.Task, task)
                ? new(existing.Task, existing.ETag, TaskChangeRejection.None)
                : new(null, null, TaskChangeRejection.IdConflict);
        }

        return await GuardAsync(userId, task.Id, task, null, workspaceRevision, TaskGuardOperationKind.Create, cancellationToken);
    }

    public async Task<TaskWriteResult> UpdateAsync(string userId, TaskRecord task, string revision, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        var workspace = await ReadWorkspaceAsync(userId, cancellationToken);

        if (workspace?.TasksAuthoritative != true || workspace.PendingTaskOperation is not null || workspace.PendingProjectArchive is not null)
        {
            return new(null, null, TaskChangeRejection.StorageNotReady);
        }

        var document = await database.Tasks.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == task.Id, cancellationToken);

        if (document?.Task is null || document.IsDeleted)
        {
            return new(null, null, TaskChangeRejection.NotFound);
        }

        if (document.ETag != revision)
        {
            return new(null, null, TaskChangeRejection.Conflict);
        }

        // These values belong to the original task and focus accounting, not the edit form.
        task.CreatedAt = document.Task.CreatedAt;
        task.FocusMinutesToday = document.Task.FocusMinutesToday;

        if (task.ProjectId != document.Task.ProjectId)
        {
            database.ChangeTracker.Clear();

            return await GuardAsync(userId, task.Id, task, revision, workspaceRevision, TaskGuardOperationKind.Move, cancellationToken);
        }

        document.Task = task;

        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return new(document.Task, document.ETag, TaskChangeRejection.None);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            return new(null, null, TaskChangeRejection.Conflict);
        }
    }

    public async Task<TaskWriteResult> DeleteAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        await RecoverAsync(userId, cancellationToken);

        return await GuardAsync(userId, id, null, revision, workspaceRevision, TaskGuardOperationKind.Delete, cancellationToken);
    }

    private async Task<TaskWriteResult> GuardAsync(string userId, string id, TaskRecord? task, string? revision, string workspaceRevision,
        TaskGuardOperationKind kind, CancellationToken cancellationToken)
    {
        database.ChangeTracker.Clear();
        var workspace = await database.Workspaces.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);

        if (workspace?.TasksAuthoritative != true || !workspace.ProjectsAuthoritative)
        {
            return new(null, null, TaskChangeRejection.StorageNotReady);
        }

        if (workspace.PendingTaskOperation is not null || workspace.PendingProjectArchive is not null ||
            string.IsNullOrWhiteSpace(workspaceRevision) || workspace.ETag != workspaceRevision)
        {
            return new(null, null, TaskChangeRejection.Conflict);
        }

        var document = await database.Tasks.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);

        if (kind == TaskGuardOperationKind.Create && document is not null)
        {
            return !document.IsDeleted && document.Task is not null && task is not null && Equivalent(document.Task, task)
                ? new(document.Task, document.ETag, TaskChangeRejection.None)
                : new(null, null, TaskChangeRejection.IdConflict);
        }

        if (kind != TaskGuardOperationKind.Create && (document?.Task is null || document.IsDeleted))
        {
            if (kind == TaskGuardOperationKind.Delete && document?.IsDeleted == true && document.DeleteOriginRevision == revision)
            {
                return new(document.Task, document.ETag, TaskChangeRejection.None, workspace.ETag, WorkspaceStorageFormat.Read(workspace).Timer);
            }

            return new(null, null, TaskChangeRejection.NotFound);
        }

        if (kind != TaskGuardOperationKind.Create && document!.ETag != revision)
        {
            return new(null, null, TaskChangeRejection.Conflict);
        }

        if (kind == TaskGuardOperationKind.Create)
        {
            var existingTasks = await database.Tasks.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(cancellationToken);

            if (existingTasks.Count(item => !item.IsDeleted) >= 10_000 || existingTasks.Any(item => item.Position == int.MaxValue))
            {
                return new(null, null, TaskChangeRejection.Capacity);
            }
        }

        if (task is not null)
        {
            var project = await database.Projects.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == task.ProjectId, cancellationToken);

            if (project?.Project is null || project.Project.IsArchived)
            {
                return new(null, null, TaskChangeRejection.ProjectUnavailable);
            }
        }

        var timer = WorkspaceStorageFormat.Read(workspace).Timer;

        if (kind != TaskGuardOperationKind.Create && timer.TaskId == id && timer.Phase != TimerPhase.Idle)
        {
            return new(null, null, TaskChangeRejection.TimerActive);
        }

        var operation = new TaskGuardOperation
        {
            OperationId = Guid.NewGuid().ToString("N"),
            Kind = kind,
            TaskId = id,
            OriginRevision = revision,
            Task = task
        };
        workspace.PendingTaskOperation = operation;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            database.ChangeTracker.Clear();

            return new(null, null, TaskChangeRejection.Conflict);
        }

        return await RecoverAsync(userId, cancellationToken) ?? new(null, null, TaskChangeRejection.Conflict);
    }

    public async Task<TaskWriteResult?> RecoverAsync(string userId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            database.ChangeTracker.Clear();
            var workspace = await database.Workspaces.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);
            var operation = workspace?.PendingTaskOperation;

            if (workspace is null || operation is null)
            {
                return null;
            }

            var document = await database.Tasks.SingleOrDefaultAsync(item => item.UserId == userId && item.Id == operation.TaskId, cancellationToken);
            var alreadyApplied = document?.LastOperationId == operation.OperationId;
            var valid = alreadyApplied || (operation.Kind == TaskGuardOperationKind.Create
                ? document is null && operation.Task is not null
                : document?.Task is not null && !document.IsDeleted && document.ETag == operation.OriginRevision);

            if (!valid)
            {
                workspace.PendingTaskOperation = null;

                try
                {
                    await database.SaveChangesAsync(cancellationToken);

                    return new(null, null, TaskChangeRejection.Conflict);
                }
                catch (DbUpdateException)
                {
                    continue;
                }
            }

            if (!alreadyApplied)
            {
                if (operation.Kind == TaskGuardOperationKind.Create)
                {
                    var positions = await database.Tasks.AsNoTracking().Where(item => item.UserId == userId).Select(item => item.Position).ToListAsync(cancellationToken);
                    document = new WorkspaceTaskDocument
                    {
                        UserId = userId,
                        Id = operation.TaskId,
                        Position = positions.Count == 0 ? 0 : positions.Max() + 1,
                        Task = Copy(operation.Task!),
                        LastOperationId = operation.OperationId
                    };
                    database.Tasks.Add(document);
                }
                else
                {
                    document!.LastOperationId = operation.OperationId;

                    if (operation.Kind == TaskGuardOperationKind.Delete)
                    {
                        document.IsDeleted = true;
                        document.DeleteOriginRevision = operation.OriginRevision;
                    }
                    else
                    {
                        document.Task = Copy(operation.Task!);
                    }
                }

                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    continue;
                }
            }

            var snapshot = WorkspaceStorageFormat.Read(workspace);
            var timer = snapshot.Timer;

            if (operation.Kind == TaskGuardOperationKind.Delete && document?.Task is not null)
            {
                foreach (var session in snapshot.Sessions.Where(item => item.TaskId == operation.TaskId && item.TaskName is null))
                {
                    session.TaskName = document.Task.Title;
                }
            }

            if (timer.Phase == TimerPhase.Idle && timer.TaskId == operation.TaskId)
            {
                if (operation.Kind == TaskGuardOperationKind.Delete)
                {
                    timer.TaskId = null;
                    timer.ProjectId = "";
                }
                else if (operation.Kind == TaskGuardOperationKind.Move)
                {
                    timer.ProjectId = operation.Task!.ProjectId;
                }
            }

            workspace.Workspace = snapshot;
            workspace.Payload = null;
            workspace.PendingTaskOperation = null;
            workspace.UpdatedAt = DateTimeOffset.UtcNow;

            try
            {
                await database.SaveChangesAsync(cancellationToken);

                return new(document!.Task, document.ETag, TaskChangeRejection.None, workspace.ETag, timer);
            }
            catch (DbUpdateException)
            {
                // Another recovery may have completed the operation.
            }
        }

        return new(null, null, TaskChangeRejection.Conflict);
    }

    private Task<WorkspaceDocument?> ReadWorkspaceAsync(string userId, CancellationToken cancellationToken) =>
        database.Workspaces.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);

    private static TaskRecord Copy(TaskRecord task) =>
        JsonSerializer.Deserialize<TaskRecord>(JsonSerializer.Serialize(task, SharedCommon.JsonOptions), SharedCommon.JsonOptions)!;

    private static bool Equivalent(TaskRecord left, TaskRecord right) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(left, SharedCommon.JsonOptions), JsonSerializer.SerializeToElement(right, SharedCommon.JsonOptions));
}
