using Shared.Models;

namespace Services.Repositories;

public interface IWorkspaceItemsRepository
{
    Task<IReadOnlyList<ProjectRecord>> ListProjectsAsync(string userId, CancellationToken cancellationToken = default);
    Task<ProjectRecord?> GetProjectAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TaskRecord>> ListTasksAsync(string userId, CancellationToken cancellationToken = default);
    Task<TaskRecord?> GetTaskAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FocusSessionRecord>> ListSessionsAsync(string userId, CancellationToken cancellationToken = default);
    Task<FocusSessionRecord?> GetSessionAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConversationRecord>> ListConversationsAsync(string userId, CancellationToken cancellationToken = default);
    Task<ConversationRecord?> GetConversationAsync(string userId, string id, CancellationToken cancellationToken = default);
}
