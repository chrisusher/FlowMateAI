using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceTaskManager(WorkspaceStore store, WorkspaceStatistics statistics, NavigationManager navigation, WorkspaceItemsClient items) : WorkspaceManager
{
    private WorkspaceStore Store => store;
    private string? _editingBaseline;
    private string? _removeBaseline;
    public bool Saving { get; private set; }

    public TaskRecord Draft { get; private set; } = new();
    public bool Editing
    {
        get; private set;
    }
    public bool DialogOpen
    {
        get; private set;
    }
    public string? ValidationMessage
    {
        get; private set;
    }
    public string? ActionMessage
    {
        get; private set;
    }
    public string? RemoveTaskId
    {
        get; private set;
    }

    public async Task ToggleComplete(TaskRecord task)
    {
        var candidate = Copy(task);
        candidate.IsComplete = !candidate.IsComplete;

        if (candidate.IsComplete)
        {
            candidate.PlannedToday = true;
        }

        await ChangeTaskAsync(task, candidate);
    }

    public async Task TogglePlan(TaskRecord task)
    {
        var candidate = Copy(task);
        candidate.PlannedToday = !candidate.PlannedToday;
        await ChangeTaskAsync(task, candidate);
    }

    public async Task ToggleAllPlan()
    {
        if (Saving)
        {
            return;
        }

        var changes = Store.Data.Tasks.Select(Copy).ToArray();
        var failures = new List<string>();

        foreach (var original in changes)
        {
            var candidate = Copy(original);
            candidate.PlannedToday = !candidate.PlannedToday;

            if (!await ChangeTaskAsync(original, candidate))
            {
                failures.Add(original.Title);
            }
        }

        ActionMessage = failures.Count == 0 ? "Planning updated." : $"Could not update: {string.Join(", ", failures)}. Reload and retry those tasks.";
        NotifyChanged();
    }

    private async Task<bool> ChangeTaskAsync(TaskRecord original, TaskRecord candidate)
    {
        if (Saving)
        {
            return false;
        }

        var baseline = WorkspaceCollectionManifest.Hash(new[] { original });
        Saving = true;
        ActionMessage = "Saving task…";
        NotifyChanged();

        try
        {
            await Store.WriteTaskAsync(original.Id, false, async () =>
            {
                var remote = await RequireUnchangedAsync(original.Id, baseline);

                return await items.UpdateTaskAsync(candidate, remote.Revision, Store.WorkspaceRevision ?? "");
            });
            ActionMessage = "Task saved.";

            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or TaskCanceledException)
        {
            ActionMessage = exception is TaskCanceledException ? "The task request timed out. Reload before retrying." : exception.Message;

            return false;
        }
        finally
        {
            Saving = false;
            NotifyChanged();
        }
    }

    private async Task<TaskWriteResponse> RequireUnchangedAsync(string id, string baseline)
    {
        var local = Store.Task(id);
        var remote = await items.GetTaskVersionedAsync(id);

        if (local is null || remote is null || WorkspaceCollectionManifest.Hash(new[] { local }) != baseline ||
            WorkspaceCollectionManifest.Hash(new[] { remote.Task }) != baseline)
        {
            throw new InvalidOperationException("This task changed on another device. Reload it before editing.");
        }

        return remote;
    }

    private static TaskRecord Copy(TaskRecord task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        ProjectId = task.ProjectId,
        DueDate = task.DueDate,
        Priority = task.Priority,
        IsComplete = task.IsComplete,
        PlannedToday = task.PlannedToday,
        FocusMinutesToday = task.FocusMinutesToday,
        CreatedAt = task.CreatedAt
    };

    public async Task SelectTask(TaskRecord task)
    {
        if (Store.Data.Timer.Phase != TimerPhase.Idle && Store.Data.Timer.TaskId != task.Id)
        {
            ActionMessage = "Finish the current focus session or break before choosing another task.";
            NotifyChanged();

            return;
        }

        if (Store.Project(task.ProjectId) is not { IsArchived: false })
        {
            ActionMessage = "Move this task to an active project before focusing it.";
            NotifyChanged();

            return;
        }

        ActionMessage = null;
        Store.Data.Timer.TaskId = task.Id;
        Store.Data.Timer.ProjectId = task.ProjectId;
        await Store.SaveAsync();
    }

    public async Task CycleTask()
    {
        var tasks = statistics.PlannedTasks.Where(t => !t.IsComplete).ToList();

        if (tasks.Count == 0)
        {
            navigation.NavigateTo("tasks");

            return;
        }
        var ix = tasks.FindIndex(t => t.Id == Store.Data.Timer.TaskId);
        var next = tasks[(ix + 1) % tasks.Count];
        await SelectTask(next);
    }

    public Task AddQuickTask()
    {
        var project = Store.Data.Projects.FirstOrDefault(project => !project.IsArchived);
        ValidationMessage = null;
        ActionMessage = null;
        Draft = new()
        {
            ProjectId = project?.Id ?? "",
            PlannedToday = true
        };
        Editing = false;
        DialogOpen = true;
        NotifyChanged();

        return Task.CompletedTask;
    }

    public void AddTaskForProject(string projectId)
    {
        if (Store.Project(projectId) is not { IsArchived: false })
        {
            ActionMessage = "Choose an active project for this task.";
            NotifyChanged();

            return;
        }

        ValidationMessage = null;
        ActionMessage = null;
        Draft = new()
        {
            ProjectId = projectId
        };
        Editing = false;
        DialogOpen = true;
        NotifyChanged();
    }

    public void OpenTaskEditor(TaskRecord task)
    {
        ValidationMessage = null;
        ActionMessage = null;
        Draft = new()
        {
            Id = task.Id,
            Title = task.Title,
            ProjectId = task.ProjectId,
            DueDate = task.DueDate,
            Priority = task.Priority,
            IsComplete = task.IsComplete,
            PlannedToday = task.PlannedToday,
            FocusMinutesToday = task.FocusMinutesToday,
            CreatedAt = task.CreatedAt
        };
        _editingBaseline = WorkspaceCollectionManifest.Hash(new[] { task });
        Editing = true;
        DialogOpen = true;
        NotifyChanged();
    }

    public void CloseTaskDialog()
    {
        if (Saving)
        {
            return;
        }

        DialogOpen = false;
        ValidationMessage = null;
        NotifyChanged();
    }

    public void HandleDialogKeyDown(KeyboardEventArgs args)
    {
        if (!Saving && args.Key == "Escape")
        {
            DialogOpen = false;
            RemoveTaskId = null;
            ValidationMessage = null;
            NotifyChanged();
        }
    }

    public async Task SaveTask(EditContext _)
    {
        var title = (Draft.Title ?? string.Empty).Trim();

        if (title.Length == 0 || title.Length > 160 || title.Any(char.IsControl))
        {
            ValidationMessage = title.Length == 0
                ? "Enter a task name."
                : title.Length > 160
                    ? "Task names can be up to 160 characters."
                    : "Task names cannot contain control characters.";
            NotifyChanged();

            return;
        }

        var project = Store.Project(Draft.ProjectId);
        var existingTask = Editing ? Store.Task(Draft.Id) : null;

        if (project is null || (project.IsArchived && existingTask?.ProjectId != project.Id))
        {
            ValidationMessage = "Choose an active project for this task.";
            NotifyChanged();

            return;
        }

        if (Editing && existingTask is not null && existingTask.ProjectId != Draft.ProjectId &&
            TimerReferencesTask(existingTask) && IsTimerRunningOrPaused())
        {
            ValidationMessage = "Finish this task's timer before moving it.";
            NotifyChanged();

            return;
        }

        if (Saving)
        {
            return;
        }

        Draft.Title = title;
        Saving = true;
        ValidationMessage = null;
        ActionMessage = "Saving task…";
        NotifyChanged();

        try
        {
            var candidate = Copy(Draft);
            await Store.WriteTaskAsync(candidate.Id, false, async () =>
            {
                if (!Editing)
                {
                    var created = await items.CreateTaskAsync(candidate, Store.WorkspaceRevision ?? "");

                    if (created.WorkspaceRevision is null || created.Timer is null)
                    {
                        throw new InvalidOperationException("The task may already have been saved. Reload the workspace to check it.");
                    }

                    return created;
                }

                var remote = await RequireUnchangedAsync(candidate.Id, _editingBaseline ?? "");

                return await items.UpdateTaskAsync(candidate, remote.Revision, Store.WorkspaceRevision ?? "");
            });
            DialogOpen = false;
            _editingBaseline = null;
            ActionMessage = "Task saved.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or TaskCanceledException)
        {
            ValidationMessage = exception is TaskCanceledException ? "The task request timed out. Reload before retrying." : exception.Message;
            ActionMessage = null;
        }
        finally
        {
            Saving = false;
            NotifyChanged();
        }
    }

    public void RequestRemoveTask(TaskRecord task)
    {
        ActionMessage = null;

        if (TimerReferencesTask(task) && IsTimerRunningOrPaused())
        {
            ActionMessage = "Finish this task's timer before removing it.";
            NotifyChanged();

            return;
        }

        RemoveTaskId = task.Id;
        _removeBaseline = WorkspaceCollectionManifest.Hash(new[] { task });
        NotifyChanged();
    }

    public void CloseRemoveTaskDialog()
    {
        if (Saving)
        {
            return;
        }

        RemoveTaskId = null;
        NotifyChanged();
    }

    public async Task ConfirmRemoveTaskAsync()
    {
        var task = Store.Task(RemoveTaskId ?? "");

        if (task is null)
        {
            CloseRemoveTaskDialog();

            return;
        }

        if (TimerReferencesTask(task) && IsTimerRunningOrPaused())
        {
            RemoveTaskId = null;
            ActionMessage = "Finish this task's timer before removing it.";
            NotifyChanged();

            return;
        }

        if (Saving)
        {
            return;
        }

        Saving = true;
        ActionMessage = "Removing task…";
        NotifyChanged();

        try
        {
            await Store.WriteTaskAsync(task.Id, true, async () =>
            {
                var remote = await RequireUnchangedAsync(task.Id, _removeBaseline ?? "");

                return await items.DeleteTaskAsync(task.Id, remote.Revision, Store.WorkspaceRevision ?? "");
            });
            RemoveTaskId = null;
            _removeBaseline = null;
            ActionMessage = "Task removed. Focus history is preserved.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or TaskCanceledException)
        {
            ActionMessage = exception is TaskCanceledException ? "The task request timed out. Reload before retrying." : exception.Message;
        }
        finally
        {
            Saving = false;
            NotifyChanged();
        }
    }

    private bool TimerReferencesTask(TaskRecord task) =>
        string.Equals(Store.Data.Timer.TaskId, task.Id, StringComparison.Ordinal);

    private bool IsTimerRunningOrPaused() => Store.Data.Timer.Phase is
        TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused;
}
