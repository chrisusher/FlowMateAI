using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

public sealed class WorkspaceItemsRepository(DatabaseContext database) : IWorkspaceItemsRepository
{
    public Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return ListAsync<WorkspaceProjectDocument, ProjectRecord>(database.Projects, userId, workspace => workspace.Projects, cancellationToken);
    }


    public Task<ProjectRecord?> GetProjectAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        return GetAsync<WorkspaceProjectDocument, ProjectRecord>(database.Projects, userId, id, cancellationToken);
    }


    public Task<IReadOnlyList<TaskRecord>> ListTasksAsync(string userId, CancellationToken cancellationToken = default)
    {
        return ListAsync<WorkspaceTaskDocument, TaskRecord>(database.Tasks, userId, workspace => workspace.Tasks, cancellationToken);
    }


    public Task<TaskRecord?> GetTaskAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        return GetAsync<WorkspaceTaskDocument, TaskRecord>(database.Tasks, userId, id, cancellationToken);
    }


    public Task<IReadOnlyList<FocusSessionRecord>> ListSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return ListAsync<WorkspaceSessionDocument, FocusSessionRecord>(database.Sessions, userId, workspace => workspace.Sessions, cancellationToken);
    }


    public Task<FocusSessionRecord?> GetSessionAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        return GetAsync<WorkspaceSessionDocument, FocusSessionRecord>(database.Sessions, userId, id, cancellationToken);
    }


    public Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return ListAsync<WorkspaceConversationDocument, ConversationRecord>(database.Conversations, userId, workspace => workspace.Conversations, cancellationToken);
    }


    public Task<ConversationRecord?> GetConversationAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        return GetAsync<WorkspaceConversationDocument, ConversationRecord>(database.Conversations, userId, id, cancellationToken);
    }


    private async Task<IReadOnlyList<TRecord>> ListAsync<TDocument, TRecord>(DbSet<TDocument> set, string userId, Func<WorkspaceSnapshot, List<TRecord>> select, CancellationToken cancellationToken)
        where TDocument : class, IWorkspaceItemDocument
        where TRecord : class
    {
        var documents = await set.AsNoTracking().Where(item => item.UserId == userId)
            .OrderBy(item => item.Position).ToListAsync(cancellationToken);

        var records = documents.Select(item => item.Value as TRecord ?? throw new InvalidOperationException("A workspace record has no data.")).ToArray();

        var source = await database.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);

        var authoritative = select(source is null ? new WorkspaceSnapshot() : WorkspaceStorageFormat.Read(source));

        // Legacy accounts and interrupted projection updates must remain readable while migration is underway.

        return WorkspaceCollectionManifest.Hash(records) == WorkspaceCollectionManifest.Hash(authoritative) ? records : authoritative;
    }

    private static async Task<TRecord?> GetAsync<TDocument, TRecord>(DbSet<TDocument> set, string userId, string id, CancellationToken cancellationToken)
        where TDocument : class, IWorkspaceItemDocument
        where TRecord : class
    {
        var document = await set
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);

        return document is null ? null : document.Value as TRecord ?? throw new InvalidOperationException("A workspace record has no data.");
    }
}
