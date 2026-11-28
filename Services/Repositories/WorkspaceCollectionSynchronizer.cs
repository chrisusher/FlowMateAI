using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

/// <summary>Synchronises the collection projections from the authoritative workspace.</summary>
public sealed class WorkspaceCollectionSynchronizer(DatabaseContext database)
{
    public async Task SynchroniseAsync(string userId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            database.ChangeTracker.Clear();
            var source = await database.Workspaces.AsNoTracking().SingleAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);
            var workspace = WorkspaceStorageFormat.Read(source);

            try
            {
                if (!source.ProjectsAuthoritative)
                {
                    await SyncAsync(database.Projects, userId, workspace.Projects, item => item.Id,
                        item => new WorkspaceProjectDocument
                        {
                            Project = item
                        }, (document, item) => document.Project = item, cancellationToken);
                }

                if (!source.TasksAuthoritative)
                {
                    await SyncAsync(database.Tasks, userId, workspace.Tasks, item => item.Id,
                        item => new WorkspaceTaskDocument
                        {
                            Task = item
                        }, (document, item) => document.Task = item, cancellationToken);
                }
                await SyncAsync(database.Sessions, userId, workspace.Sessions, item => item.Id,
                    item => new WorkspaceSessionDocument
                    {
                        Session = item
                    }, (document, item) => document.Session = item, cancellationToken);
                await SyncAsync(database.Conversations, userId, workspace.Conversations, item => item.Id,
                    item => new WorkspaceConversationDocument
                    {
                        Conversation = item
                    }, (document, item) => document.Conversation = item, cancellationToken);

                var current = await database.Workspaces.AsNoTracking().SingleAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);

                if (current.ETag == source.ETag && current.UpdatedAt == source.UpdatedAt)
                {
                    return;
                }
            }
            catch (DbUpdateException) when (attempt < 4)
            {
                // A competing synchronisation may have changed a projection. Reload and retry.
            }
        }

        throw new InvalidOperationException("Workspace collections could not be synchronised after concurrent changes. Retry the save.");
    }

    private async Task SyncAsync<TDocument, TRecord>(DbSet<TDocument> set, string userId, IReadOnlyList<TRecord> records,
        Func<TRecord, string> getId, Func<TRecord, TDocument> create, Action<TDocument, TRecord> update, CancellationToken cancellationToken)
        where TDocument : class, IWorkspaceItemDocument
        where TRecord : class
    {
        var existing = await set.Where(item => item.UserId == userId).ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (var position = 0; position < records.Count; position++)
        {
            var value = records[position];
            var id = getId(value);

            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                throw new InvalidOperationException("Workspace record IDs must be present and unique within each collection.");
            }

            if (!byId.TryGetValue(id, out var stored))
            {
                stored = create(value);
                stored.Id = id;
                stored.UserId = userId;
                stored.Position = position;
                set.Add(stored);
                continue;
            }

            if (stored.Value is null || !JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(stored.Value, stored.Value.GetType(), SharedCommon.JsonOptions),
                JsonSerializer.SerializeToElement(value, SharedCommon.JsonOptions)))
            {
                update(stored, value);
            }

            stored.Position = position;
        }

        set.RemoveRange(existing.Where(item => !ids.Contains(item.Id)));
        await database.SaveChangesAsync(cancellationToken);
    }
}
