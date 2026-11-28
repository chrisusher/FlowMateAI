using Services.Repositories;
using Shared.Enums;
using Shared.Models;

namespace Services.Workspaces;

/// <summary>Validates task changes before passing them to the task store.</summary>
public sealed class TaskService(ITaskRepository tasks) : ITaskService
{
    public Task<TaskWriteResult> CreateAsync(string userId, TaskRecord task, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        if (!ValidTask(task) || !ValidRevision(workspaceRevision) || task.IsComplete || task.FocusMinutesToday != 0)
        {
            return InvalidAsync();
        }

        return tasks.CreateAsync(userId, Clean(task), workspaceRevision, cancellationToken);
    }

    public Task<TaskWriteResult> UpdateAsync(string userId, TaskRecord task, string revision, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        if (!ValidTask(task) || !ValidRevision(revision) || !ValidRevision(workspaceRevision))
        {
            return InvalidAsync();
        }

        return tasks.UpdateAsync(userId, Clean(task), revision, workspaceRevision, cancellationToken);
    }

    public Task<TaskWriteResult> DeleteAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default)
    {
        if (!ValidId(id) || !ValidRevision(revision) || !ValidRevision(workspaceRevision))
        {
            return InvalidAsync();
        }

        return tasks.DeleteAsync(userId, id, revision, workspaceRevision, cancellationToken);
    }

    private static bool ValidTask(TaskRecord? task) => task is not null && ValidId(task.Id) && ValidId(task.ProjectId) &&
        task.Title is not null && task.Title.Trim() is { Length: > 0 and <= 160 } &&
        !task.Title.Any(char.IsControl) && Enum.IsDefined(task.Priority) && task.FocusMinutesToday is >= 0 and <= 1440;

    private static bool ValidId(string? id) => id is { Length: > 0 and <= 128 } &&
        !string.IsNullOrWhiteSpace(id) && !id.Any(character => char.IsControl(character) || character is '/' or '\\' or '?' or '#');

    private static bool ValidRevision(string? revision) => !string.IsNullOrWhiteSpace(revision) && revision.Length <= 512;

    private static Task<TaskWriteResult> InvalidAsync() => Task.FromResult(new TaskWriteResult(null, null, TaskChangeRejection.InvalidInput));

    private static TaskRecord Clean(TaskRecord task) => new()
    {
        Id = task.Id,
        Title = task.Title.Trim(),
        ProjectId = task.ProjectId,
        DueDate = task.DueDate,
        Priority = task.Priority,
        IsComplete = task.IsComplete,
        PlannedToday = task.PlannedToday,
        FocusMinutesToday = task.FocusMinutesToday,
        CreatedAt = task.CreatedAt
    };
}
