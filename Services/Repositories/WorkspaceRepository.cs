using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;
using System.Text.Json;

namespace Services.Repositories;

public sealed class WorkspaceRepository(DatabaseContext database) : IWorkspaceRepository
{
    private static readonly SemaphoreSlim InitializationLock = new(1, 1);
    private static bool _initialized;

    public async Task<WorkspaceDocument?> GetAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        return await database.Workspaces.FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == ownerId, cancellationToken);
    }

    public async Task<WorkspaceDocument?> SaveAsync(string ownerId, WorkspaceSnapshot workspace, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken);
        var document = await database.Workspaces.FirstOrDefaultAsync(x => x.Id == "workspace" && x.UserId == ownerId, cancellationToken);
        if (document is not null && !string.IsNullOrEmpty(expectedRevision) && document.ETag != expectedRevision) return null;
        if (document is null)
        {
            document = new WorkspaceDocument { Id = "workspace", UserId = ownerId };
            database.Workspaces.Add(document);
        }
        document.Payload = JsonSerializer.Serialize(workspace, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        document.UpdatedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        await SynchronizeRecordsAsync(ownerId, workspace, cancellationToken);
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

    private async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await InitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await database.Database.EnsureCreatedAsync(cancellationToken);
            _initialized = true;
        }
        finally { InitializationLock.Release(); }
    }
}
