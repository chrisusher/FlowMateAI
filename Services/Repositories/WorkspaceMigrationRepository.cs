using System.Text.Json;
using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

/// <summary>Copies a legacy workspace into its collection containers without deleting its source.</summary>
public sealed class WorkspaceMigrationRepository(DatabaseContext database)
{
    public async Task<WorkspaceMigrationResult> MigrateAsync(string userId, bool apply = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var source = await database.Workspaces.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.Id == "workspace", cancellationToken)
            ?? throw new InvalidOperationException("The workspace was not found.");
        var snapshot = WorkspaceStorageFormat.Read(source);
        var sourceSnapshot = JsonSerializer.Serialize(snapshot, SharedCommon.JsonOptions);

        var projects = Build(userId, snapshot.Projects, item => item.Id, item => new WorkspaceProjectDocument { Project = item });
        var tasks = Build(userId, snapshot.Tasks, item => item.Id, item => new WorkspaceTaskDocument { Task = item });
        var sessions = Build(userId, snapshot.Sessions, item => item.Id, item => new WorkspaceSessionDocument { Session = item });
        var conversations = Build(userId, snapshot.Conversations, item => item.Id, item => new WorkspaceConversationDocument { Conversation = item });

        // Check every destination before writing anything. Existing identical records are safe to resume.
        await CheckAsync(database.WorkspaceProjects, userId, projects, cancellationToken);
        await CheckAsync(database.WorkspaceTasks, userId, tasks, cancellationToken);
        await CheckAsync(database.WorkspaceSessions, userId, sessions, cancellationToken);
        await CheckAsync(database.WorkspaceConversations, userId, conversations, cancellationToken);

        if (!apply)
        {
            return new(projects.Count, tasks.Count, sessions.Count, conversations.Count);
        }

        await CopyAsync(database.WorkspaceProjects, userId, projects, cancellationToken);
        await CopyAsync(database.WorkspaceTasks, userId, tasks, cancellationToken);
        await CopyAsync(database.WorkspaceSessions, userId, sessions, cancellationToken);
        await CopyAsync(database.WorkspaceConversations, userId, conversations, cancellationToken);

        await VerifyAsync(database.WorkspaceProjects, userId, projects, cancellationToken);
        await VerifyAsync(database.WorkspaceTasks, userId, tasks, cancellationToken);
        await VerifyAsync(database.WorkspaceSessions, userId, sessions, cancellationToken);
        await VerifyAsync(database.WorkspaceConversations, userId, conversations, cancellationToken);
        var current = await database.Workspaces.AsNoTracking().SingleAsync(x => x.UserId == userId && x.Id == "workspace", cancellationToken);

        if (JsonSerializer.Serialize(WorkspaceStorageFormat.Read(current), SharedCommon.JsonOptions) != sourceSnapshot || current.ETag != source.ETag)
        {
            throw new InvalidOperationException("The source workspace changed during migration. Stop all writers before retrying.");
        }

        return new(projects.Count, tasks.Count, sessions.Count, conversations.Count);
    }

    private static List<TDocument> Build<TDocument, TRecord>(string userId, List<TRecord> records, Func<TRecord, string> id, Func<TRecord, TDocument> create)
        where TDocument : class, IWorkspaceItemDocument
    {
        if (records is null)
        {
            throw new InvalidOperationException("The workspace contains a null collection.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);

        return records.Select((item, position) =>
        {
            if (item is null)
            {
                throw new InvalidOperationException("The workspace contains a null record.");
            }
            var recordId = id(item);

            if (string.IsNullOrWhiteSpace(recordId) || !ids.Add(recordId))
            {
                throw new InvalidOperationException("Workspace record IDs must be present and unique within each collection.");
            }

            var document = create(item);
            document.Id = recordId;
            document.UserId = userId;
            document.Position = position;

            return document;
        }).ToList();
    }

    private static async Task CheckAsync<T>(DbSet<T> set, string userId, List<T> desired, CancellationToken token) where T : class, IWorkspaceItemDocument
    {
        var existing = await set.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(token);
        var expected = desired.ToDictionary(x => x.Id, StringComparer.Ordinal);

        if (existing.Any(item => !expected.TryGetValue(item.Id, out var wanted) || !Equivalent(item, wanted)))
        {
            throw new InvalidOperationException($"The destination {typeof(T).Name} contains different or unexpected records; no records will be overwritten.");
        }
    }

    private async Task CopyAsync<T>(DbSet<T> set, string userId, List<T> desired, CancellationToken token) where T : class, IWorkspaceItemDocument
    {
        var existing = (await set.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);

        foreach (var item in desired.Where(item => !existing.Contains(item.Id)))
        {
            set.Add(item);
            await database.SaveChangesAsync(token);
            database.Entry(item).State = EntityState.Detached;
        }
    }

    private static async Task VerifyAsync<T>(DbSet<T> set, string userId, List<T> desired, CancellationToken token) where T : class, IWorkspaceItemDocument
    {
        var actual = await set.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(token);
        var expected = desired.ToDictionary(x => x.Id, StringComparer.Ordinal);

        if (actual.Count != desired.Count || actual.Any(item => !expected.TryGetValue(item.Id, out var wanted) || !Equivalent(item, wanted)))
        {
            throw new InvalidOperationException($"Verification failed for {typeof(T).Name}.");
        }
    }

    private static bool Equivalent(IWorkspaceItemDocument left, IWorkspaceItemDocument right) =>
        left.Position == right.Position && left.Value is not null && right.Value is not null &&
        JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left.Value, left.Value.GetType(), SharedCommon.JsonOptions), JsonSerializer.SerializeToElement(right.Value, right.Value.GetType(), SharedCommon.JsonOptions));
}

/// <summary>Number of records verified in each migrated collection.</summary>
public sealed record WorkspaceMigrationResult(int Projects, int Tasks, int Sessions, int Conversations);
