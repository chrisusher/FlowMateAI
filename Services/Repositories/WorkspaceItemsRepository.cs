using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

public sealed class WorkspaceItemsRepository(DatabaseContext database) : IWorkspaceItemsRepository
{
    public async Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var source = await database.Workspaces.AsNoTracking()
            .FirstOrDefaultAsync(item => item.UserId == userId && item.Id == "workspace", cancellationToken);

        if (source?.ProjectsAuthoritative == true)
        {
            var projects = await database.Projects.AsNoTracking().Where(item => item.UserId == userId)
                .OrderBy(item => item.Position).ThenBy(item => item.Id).ToListAsync(cancellationToken);

            return projects.Select(item => item.Project ?? throw new InvalidDataException("A project document has no project data.")).ToArray();
        }

        return await ListAsync<WorkspaceProjectDocument, ProjectRecord>(database.Projects, userId, workspace => workspace.Projects, cancellationToken);
    }


    public async Task<ProjectRecord?> GetProjectAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        var projects = await ListProjectsAsync(userId, cancellationToken);

        return projects.SingleOrDefault(item => item.Id == id);
    }


    public Task<IReadOnlyList<TaskRecord>> ListTasksAsync(string userId, CancellationToken cancellationToken = default)
    {
        return ListAsync<WorkspaceTaskDocument, TaskRecord>(database.Tasks, userId, workspace => workspace.Tasks, cancellationToken);
    }


    public Task<TaskRecord?> GetTaskAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        return GetAsync<WorkspaceTaskDocument, TaskRecord>(database.Tasks, userId, id, workspace => workspace.Tasks, item => item.Id, cancellationToken);
    }


    public Task<IReadOnlyList<FocusSessionRecord>> ListSessionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return ListAsync<WorkspaceSessionDocument, FocusSessionRecord>(database.Sessions, userId, workspace => workspace.Sessions, cancellationToken);
    }


    public Task<FocusSessionRecord?> GetSessionAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        return GetAsync<WorkspaceSessionDocument, FocusSessionRecord>(database.Sessions, userId, id, workspace => workspace.Sessions, item => item.Id, cancellationToken);
    }


    public Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return ListAsync<WorkspaceConversationDocument, ConversationRecord>(database.Conversations, userId, workspace => workspace.Conversations, cancellationToken);
    }


    public Task<ConversationRecord?> GetConversationAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        return GetAsync<WorkspaceConversationDocument, ConversationRecord>(database.Conversations, userId, id, workspace => workspace.Conversations, item => item.Id, cancellationToken);
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

    private async Task<TRecord?> GetAsync<TDocument, TRecord>(DbSet<TDocument> set, string userId, string id,
        Func<WorkspaceSnapshot, List<TRecord>> select, Func<TRecord, string> getId, CancellationToken cancellationToken)
        where TDocument : class, IWorkspaceItemDocument
        where TRecord : class
    {
        var records = await ListAsync<TDocument, TRecord>(set, userId, select, cancellationToken);

        return records.SingleOrDefault(item => getId(item) == id);
    }
}
