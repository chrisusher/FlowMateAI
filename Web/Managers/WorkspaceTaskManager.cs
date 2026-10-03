using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Shared.Models;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceTaskManager(WorkspaceStore store, WorkspaceStatistics statistics, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    public TaskRecord Draft { get; private set; } = new();
    public bool Editing { get; private set; }
    public bool DialogOpen { get; private set; }

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
        var project = Store.Data.Projects.FirstOrDefault();
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
        NotifyChanged();
    }

    public async Task SaveTask(EditContext _)
    {
        if (string.IsNullOrWhiteSpace(Draft.Title))
        {
            return;
        }

        if (Editing)
        {
            var index = Store.Data.Tasks.FindIndex(t => t.Id == Draft.Id);

            if (index >= 0)
            {
                Store.Data.Tasks[index] = Draft;
            }
        }
        else
        {
            Store.Data.Tasks.Add(Draft);
        }

        DialogOpen = false;
        NotifyChanged();
        await Store.SaveAsync();
    }
}
