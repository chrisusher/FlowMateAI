using Microsoft.EntityFrameworkCore;
using Services.Database;
using Shared.Models;

namespace Services.Repositories;

public sealed class WorkspaceItemsRepository(DatabaseContext database) : IWorkspaceItemsRepository
{
    public Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(string userId, CancellationToken cancellationToken = default) =>
        ListAsync<WorkspaceProjectDocument, ProjectRecord>(database.Projects, userId, cancellationToken);

    public Task<ProjectRecord?> GetProjectAsync(string userId, string id, CancellationToken cancellationToken = default) =>
        GetAsync<WorkspaceProjectDocument, ProjectRecord>(database.Projects, userId, id, cancellationToken);

    public Task<IReadOnlyList<TaskRecord>> ListTasksAsync(string userId, CancellationToken cancellationToken = default) =>
        ListAsync<WorkspaceTaskDocument, TaskRecord>(database.Tasks, userId, cancellationToken);

    public Task<TaskRecord?> GetTaskAsync(string userId, string id, CancellationToken cancellationToken = default) =>
        GetAsync<WorkspaceTaskDocument, TaskRecord>(database.Tasks, userId, id, cancellationToken);

    public Task<IReadOnlyList<FocusSessionRecord>> ListSessionsAsync(string userId, CancellationToken cancellationToken = default) =>
        ListAsync<WorkspaceSessionDocument, FocusSessionRecord>(database.Sessions, userId, cancellationToken);

    public Task<FocusSessionRecord?> GetSessionAsync(string userId, string id, CancellationToken cancellationToken = default) =>
        GetAsync<WorkspaceSessionDocument, FocusSessionRecord>(database.Sessions, userId, id, cancellationToken);

    public Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default) =>
        ListAsync<WorkspaceConversationDocument, ConversationRecord>(database.Conversations, userId, cancellationToken);

    public Task<ConversationRecord?> GetConversationAsync(string userId, string id, CancellationToken cancellationToken = default) =>
        GetAsync<WorkspaceConversationDocument, ConversationRecord>(database.Conversations, userId, id, cancellationToken);

    private static async Task<IReadOnlyList<TRecord>> ListAsync<TDocument, TRecord>(DbSet<TDocument> set, string userId, CancellationToken cancellationToken)
        where TDocument : class, IWorkspaceItemDocument
        where TRecord : class
    {
        var documents = await set.AsNoTracking().Where(item => item.UserId == userId)
            .OrderBy(item => item.Position).ToListAsync(cancellationToken);

        return documents.Select(item => item.Value as TRecord ?? throw new InvalidOperationException("A workspace record has no data.")).ToArray();
    }

    private static async Task<TRecord?> GetAsync<TDocument, TRecord>(DbSet<TDocument> set, string userId, string id, CancellationToken cancellationToken)
        where TDocument : class, IWorkspaceItemDocument
        where TRecord : class
    {
        var document = await set.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId && item.Id == id, cancellationToken);

        return document is null ? null : document.Value as TRecord ?? throw new InvalidOperationException("A workspace record has no data.");
    }
}
