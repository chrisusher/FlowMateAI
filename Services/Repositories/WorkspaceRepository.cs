using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

public sealed class WorkspaceRepository(DatabaseContext database) : IWorkspaceRepository
{
    public async Task<WorkspaceDocument?> GetAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        return await database.Workspaces.FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == ownerId, cancellationToken);
    }

    public async Task<WorkspaceDocument?> SaveAsync(string ownerId, WorkspaceSnapshot workspace, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        var document = await database.Workspaces.FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == ownerId, cancellationToken);

        if (document is not null && (string.IsNullOrEmpty(expectedRevision) || !string.Equals(document.ETag, expectedRevision, StringComparison.Ordinal)))
        {
            return null;
        }

        if (document is null)
        {
            document = new WorkspaceDocument
            {
                Id = "workspace",
                UserId = ownerId
            };
            database.Workspaces.Add(document);
        }
        document.Payload = JsonSerializer.Serialize(workspace, SharedCommon.JsonOptions);
        document.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await SynchronizeRecordsAsync(ownerId, workspace, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new WorkspaceSaveConflictException(exception);
        }

        return document;
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
            stored.Payload = record.Payload;
        }
        await database.SaveChangesAsync(cancellationToken);
    }
}

public sealed class WorkspaceSaveConflictException(Exception innerException)
    : Exception("The workspace changed while it was being saved.", innerException);
