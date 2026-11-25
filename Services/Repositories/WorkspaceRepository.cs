using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

public sealed class WorkspaceRepository(DatabaseContext database, WorkspaceCollectionSynchronizer synchronizer) : IWorkspaceRepository
{
    public async Task<WorkspaceDocument?> GetAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var document = await database.Workspaces.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == ownerId, cancellationToken);

        if (document is not null)
        {
            document.Workspace = WorkspaceStorageFormat.Read(document);

            if (document.TasksAuthoritative)
            {
                document.Workspace.Tasks = await ReadTasksAsync(ownerId, cancellationToken);
            }

            if (document.ProjectsAuthoritative)
            {
                document.Workspace.Projects = await ReadProjectsAsync(ownerId, cancellationToken);
            }
        }

        return document;
    }

    private async Task<List<ProjectRecord>> ReadProjectsAsync(string userId, CancellationToken cancellationToken)
    {
        var documents = await database.Projects.AsNoTracking()
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);

        return documents.OrderBy(item => item.Position).ThenBy(item => item.Id, StringComparer.Ordinal).Select(item => item.Project ?? throw new InvalidDataException("A project document has no project data.")).ToList();
    }

    private async Task<List<TaskRecord>> ReadTasksAsync(string userId, CancellationToken cancellationToken)
    {
        var documents = await database.Tasks.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(cancellationToken);

        return documents.Where(item => !item.IsDeleted).OrderBy(item => item.Position).ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => item.Task ?? throw new InvalidDataException("A task document has no task data.")).ToList();
    }

    public async Task<WorkspaceDocument?> SaveAsync(string ownerId, WorkspaceSnapshot workspace, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        var existing = await database.Workspaces.FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == ownerId, cancellationToken);

        if ((existing is not null && (existing.PendingProjectArchive is not null || existing.PendingTaskOperation is not null || string.IsNullOrEmpty(expectedRevision) || existing.ETag != expectedRevision)) ||
            (existing is null && !string.IsNullOrEmpty(expectedRevision)))
        {
            return null;
        }

        var initialiseProjects = existing is null && workspace.Projects.Count == 0;
        var initialiseTasks = existing is null && workspace.Tasks.Count == 0 &&
            !await database.Tasks.AsNoTracking().AnyAsync(item => item.UserId == ownerId, cancellationToken);
        string? projectIndexId = null;

        if (initialiseProjects)
        {
            projectIndexId = await InitialiseEmptyProjectsAsync(ownerId, cancellationToken);

            if (projectIndexId is null)
            {
                return null;
            }
        }

        var document = existing ?? new WorkspaceDocument
        {
            UserId = ownerId,
            ProjectsAuthoritative = initialiseProjects,
            TasksAuthoritative = initialiseTasks,
            ProjectIndexId = projectIndexId ?? "project-index"
        };

        if (existing is null)
        {
            database.Workspaces.Add(document);
        }

        if (document.TasksAuthoritative)
        {
            // Do not restore a stale task copy into the workspace document.
            var copy = JsonSerializer.Deserialize<WorkspaceSnapshot>(JsonSerializer.Serialize(workspace))!;
            copy.Tasks.Clear();
            document.Workspace = copy;
        }
        else
        {
            document.Workspace = workspace;
        }
        document.Payload = null;
        document.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await SynchronizeRecordsAsync(ownerId, document.Workspace!, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new WorkspaceSaveConflictException(exception);
        }

        await synchronizer.SynchroniseAsync(ownerId, cancellationToken);

        return document;
    }

    private async Task<string?> InitialiseEmptyProjectsAsync(string ownerId, CancellationToken cancellationToken)
    {
        if (await database.Projects.AsNoTracking().AnyAsync(item => item.UserId == ownerId, cancellationToken))
        {
            return null;
        }

        var indexes = await database.ProjectIndexes.AsNoTracking().Where(item => item.UserId == ownerId).ToListAsync(cancellationToken);

        if (indexes.Count > 0)
        {
            return indexes.Count == 1 && indexes[0].ActiveCount == 0 && indexes[0].TotalCount == 0 && indexes[0].NextPosition == 0
                ? indexes[0].Id
                : null;
        }

        var index = new ProjectIndexDocument
        {
            Id = $"project-index-{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(ownerId)))}",
            UserId = ownerId
        };
        database.ProjectIndexes.Add(index);

        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return index.Id;
        }
        catch (DbUpdateException)
        {
            // A competing initialisation may have inserted the same index. Re-read it
            // and fail closed if it contains any existing project state.
            database.ChangeTracker.Clear();
            indexes = await database.ProjectIndexes.AsNoTracking().Where(item => item.UserId == ownerId).ToListAsync(cancellationToken);

            return indexes.Count == 1 && indexes[0].ActiveCount == 0 && indexes[0].TotalCount == 0 && indexes[0].NextPosition == 0 &&
                !await database.Projects.AsNoTracking().AnyAsync(item => item.UserId == ownerId, cancellationToken)
                ? indexes[0].Id
                : null;
        }
    }

    private async Task SynchronizeRecordsAsync(string ownerId, WorkspaceSnapshot workspace, CancellationToken cancellationToken)
    {
        var existing = await database.WorkspaceRecords.Where(record => record.UserId == ownerId).ToListAsync(cancellationToken);
        var existingById = existing.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var desired = WorkspaceRecordDocument.FromSnapshot(ownerId, workspace).ToArray();
        var desiredIds = desired.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        database.WorkspaceRecords.RemoveRange(existing.Where(record => !desiredIds.Contains(record.Id)));

        foreach (var record in desired)
        {
            if (!existingById.TryGetValue(record.Id, out var stored))
            {
                database.WorkspaceRecords.Add(record);
                continue;
            }
            stored.RecordType = record.RecordType;
            stored.Title = record.Title;
            stored.Color = record.Color;
            stored.ProjectId = record.ProjectId;
            stored.TaskId = record.TaskId;
            stored.Status = record.Status;
            stored.Priority = record.Priority;
            stored.IsComplete = record.IsComplete;
            stored.PlannedToday = record.PlannedToday;
            stored.DueDate = record.DueDate;
            stored.StartedAt = record.StartedAt;
            stored.EndedAt = record.EndedAt;
            stored.FocusMinutes = record.FocusMinutes;
            stored.Payload = null;
            stored.Project = record.Project;
            stored.Task = record.Task;
            stored.Focus = record.Focus;
            stored.Conversation = record.Conversation;
            stored.Timer = record.Timer;
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}

public sealed class WorkspaceSaveConflictException(Exception innerException)
    : Exception("The workspace changed while it was being saved.", innerException);
