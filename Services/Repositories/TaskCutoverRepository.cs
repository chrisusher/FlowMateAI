using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

/// <summary>Prepares and verifies an account's task documents for a guarded cutover.</summary>
public sealed class TaskCutoverRepository(DatabaseContext database)
{
    public async Task<TaskCutoverPreview> PreviewAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var source = await ReadWorkspaceAsync(userId, cancellationToken);

        if (source.TasksAuthoritative || source.PendingProjectArchive is not null || source.PendingTaskOperation is not null || string.IsNullOrWhiteSpace(source.ETag))
        {
            throw new InvalidOperationException("Task cutover requires a legacy workspace with a revision and no pending project archive.");
        }

        var tasks = WorkspaceStorageFormat.Read(source).Tasks;
        ValidateTasks(tasks);
        await CheckDestinationAsync(userId, tasks, false, cancellationToken);

        return new(userId, source.ETag, tasks.Count);
    }

    /// <summary>Copies and verifies tasks before changing authority. All workspace writers must be stopped and a recoverable backup made first.</summary>
    public async Task ApplyAsync(string userId, string expectedRevision, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(userId, cancellationToken);

        if (!string.Equals(preview.Revision, expectedRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workspace changed after preview; back it up and preview again.");
        }

        var source = await ReadWorkspaceAsync(userId, cancellationToken);
        var tasks = WorkspaceStorageFormat.Read(source).Tasks;

        if (!string.Equals(source.ETag, expectedRevision, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The workspace changed during task cutover.");
        }

        var existing = await database.Tasks.AsNoTracking().Where(item => item.UserId == userId).Select(item => item.Id).ToListAsync(cancellationToken);
        var existingIds = existing.ToHashSet(StringComparer.Ordinal);

        for (var position = 0; position < tasks.Count; position++)
        {
            var task = tasks[position];

            if (existingIds.Contains(task.Id))
            {
                continue;
            }

            database.Tasks.Add(new WorkspaceTaskDocument
            {
                Id = task.Id,
                UserId = userId,
                Position = position,
                Task = task
            });
            await database.SaveChangesAsync(cancellationToken);
            database.ChangeTracker.Clear();
        }

        await CheckDestinationAsync(userId, tasks, true, cancellationToken);
        database.ChangeTracker.Clear();
        var current = await database.Workspaces.SingleAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);
        var snapshot = WorkspaceStorageFormat.Read(current);

        if (current.TasksAuthoritative || current.PendingProjectArchive is not null || current.PendingTaskOperation is not null ||
            !string.Equals(current.ETag, expectedRevision, StringComparison.Ordinal) || !Equivalent(snapshot.Tasks, tasks))
        {
            throw new InvalidOperationException("The workspace changed during task cutover; no cutover was made.");
        }

        snapshot.Tasks.Clear();
        current.Workspace = snapshot;
        current.Payload = null;
        current.TasksAuthoritative = true;
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<WorkspaceDocument> ReadWorkspaceAsync(string userId, CancellationToken cancellationToken) =>
        await database.Workspaces.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken)
            ?? throw new InvalidOperationException("The workspace was not found.");

    private async Task CheckDestinationAsync(string userId, List<TaskRecord> tasks, bool requireComplete, CancellationToken cancellationToken)
    {
        var existing = await database.Tasks.AsNoTracking().Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var desired = tasks.Select((task, position) => (task, position)).ToDictionary(item => item.task.Id, StringComparer.Ordinal);

        if ((requireComplete && existing.Count != tasks.Count) || existing.Any(item =>
            !desired.TryGetValue(item.Id, out var expected) || item.Position != expected.position || item.Task is null || !Equivalent(item.Task, expected.task)))
        {
            throw new InvalidOperationException("Task documents differ from the workspace; no records were overwritten.");
        }
    }

    private static void ValidateTasks(List<TaskRecord> tasks)
    {
        if (tasks is null || tasks.Any(task => task is null || string.IsNullOrWhiteSpace(task.Id)) ||
            tasks.Select(task => task.Id).Distinct(StringComparer.Ordinal).Count() != tasks.Count)
        {
            throw new InvalidOperationException("The workspace contains invalid or duplicate task IDs.");
        }
    }

    private static bool Equivalent<T>(T left, T right) => JsonElement.DeepEquals(
        JsonSerializer.SerializeToElement(left, SharedCommon.JsonOptions), JsonSerializer.SerializeToElement(right, SharedCommon.JsonOptions));
}

/// <summary>Account-scoped task cutover preview tied to a workspace revision.</summary>
public sealed record TaskCutoverPreview(string UserId, string Revision, int TaskCount);
