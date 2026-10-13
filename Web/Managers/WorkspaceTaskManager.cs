using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Shared.Enums;
using Shared.Models;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceTaskManager(WorkspaceStore store, WorkspaceStatistics statistics, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    public TaskRecord Draft { get; private set; } = new();
    public bool Editing { get; private set; }
    public bool DialogOpen { get; private set; }
    public string? ValidationMessage { get; private set; }
    public string? ActionMessage { get; private set; }
    public string? RemoveTaskId { get; private set; }

    public async Task ToggleComplete(TaskRecord task)
    {
        task.IsComplete = !task.IsComplete;

        if (task.IsComplete)
        {
            task.PlannedToday = true;
        }

        await Store.SaveAsync();
    }

    public async Task TogglePlan(TaskRecord task)
    {
        task.PlannedToday = !task.PlannedToday;
        await Store.SaveAsync();
    }

    public async Task ToggleAllPlan()
    {
        foreach (var task in Store.Data.Tasks)
        {
            task.PlannedToday = !task.PlannedToday;
        }

        await Store.SaveAsync();
    }

    public async Task SelectTask(TaskRecord task)
    {
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
        Editing = true;
        DialogOpen = true;
        NotifyChanged();
    }

    public void CloseTaskDialog()
    {
        DialogOpen = false;
        ValidationMessage = null;
        NotifyChanged();
    }

    public void HandleDialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            DialogOpen = false;
            RemoveTaskId = null;
            ValidationMessage = null;
            NotifyChanged();
        }
    }

    public async Task SaveTask(EditContext _)
    {
        var title = Draft.Title.Trim();

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

        if (Editing)
        {
            var index = Store.Data.Tasks.FindIndex(t => t.Id == Draft.Id);

            if (index < 0)
            {
                ValidationMessage = "This task is no longer available. Reload your workspace and try again.";
                NotifyChanged();

                return;
            }

            var original = Store.Data.Tasks[index];

            if (original.ProjectId != project.Id && TimerReferencesTask(original) && IsTimerRunningOrPaused())
            {
                ValidationMessage = "Pause or finish this task's timer before moving it.";
                NotifyChanged();

                return;
            }

            Draft.Title = title;
            Store.Data.Tasks[index] = Draft;

            if (Store.Data.Timer.Phase == TimerPhase.Idle && Store.Data.Timer.TaskId == Draft.Id)
            {
                Store.Data.Timer.ProjectId = Draft.ProjectId;
            }
        }
        else
        {
            Draft.Title = title;
            Store.Data.Tasks.Add(Draft);
        }

        DialogOpen = false;
        ValidationMessage = null;
        ActionMessage = "Saving task…";
        NotifyChanged();
        await Store.SaveAsync();
        ActionMessage = Store.SyncMessage ?? "Task saved.";
        NotifyChanged();
    }

    public void RequestRemoveTask(TaskRecord task)
    {
        ActionMessage = null;

        if (TimerReferencesTask(task) && IsTimerRunningOrPaused())
        {
            ActionMessage = "Pause or finish this task's timer before removing it.";
            NotifyChanged();

            return;
        }

        RemoveTaskId = task.Id;
        NotifyChanged();
    }

    public void CloseRemoveTaskDialog()
    {
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
            ActionMessage = "Pause or finish this task's timer before removing it.";
            NotifyChanged();

            return;
        }

        if (Store.Data.Timer.Phase == TimerPhase.Idle && Store.Data.Timer.TaskId == task.Id)
        {
            Store.Data.Timer.TaskId = null;
            Store.Data.Timer.ProjectId = "";
        }

        Store.Data.Tasks.Remove(task);
        RemoveTaskId = null;
        ActionMessage = "Saving task…";
        NotifyChanged();
        await Store.SaveAsync();
        ActionMessage = Store.SyncMessage ?? "Task removed. Focus history is preserved.";
        NotifyChanged();
    }

    private bool TimerReferencesTask(TaskRecord task) =>
        string.Equals(Store.Data.Timer.TaskId, task.Id, StringComparison.Ordinal);

    private bool IsTimerRunningOrPaused() => Store.Data.Timer.Phase is
        TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused;
}
