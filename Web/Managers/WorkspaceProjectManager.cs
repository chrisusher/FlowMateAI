using Microsoft.AspNetCore.Components;
using Shared.Enums;
using Shared.Models;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceProjectManager(WorkspaceStore store, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    public string DraftName { get; set; } = "";
    public bool DialogOpen { get; private set; }
    public string? EditingProjectId { get; private set; }
    public string? ValidationMessage { get; private set; }
    public string? ActionMessage { get; private set; }
    public string? ArchiveProjectId { get; private set; }
    public int ActiveProjectCount => Store.Data.Projects.Count(project => !project.IsArchived);
    public int ProjectLimit => Store.Data.Plan == BillingPlan.Pro ? 25 : 3;

    public Task AddProject()
    {
        ActionMessage = null;

        if (ActiveProjectCount >= ProjectLimit)
        {
            navigation.NavigateTo("pricing");

            return Task.CompletedTask;
        }
        DraftName = "";
        EditingProjectId = null;
        ValidationMessage = null;
        DialogOpen = true;
        NotifyChanged();

        return Task.CompletedTask;
    }

    public void EditProject(ProjectRecord project)
    {
        DraftName = project.Name;
        EditingProjectId = project.Id;
        ValidationMessage = null;
        DialogOpen = true;
        NotifyChanged();
    }

    public void CloseProjectDialog()
    {
        DialogOpen = false;
        ValidationMessage = null;
        NotifyChanged();
    }

    public async Task SaveProject()
    {
        var name = DraftName.Trim();

        if (name.Length == 0 || name.Length > 80 || name.Any(char.IsControl))
        {
            ValidationMessage = name.Length == 0
                ? "Enter a project name."
                : name.Length > 80
                    ? "Project names can be up to 80 characters."
                    : "Project names cannot contain control characters.";
            NotifyChanged();

            return;
        }

        if (EditingProjectId is null && ActiveProjectCount >= ProjectLimit)
        {
            DialogOpen = false;
            NotifyChanged();
            navigation.NavigateTo("pricing");

            return;
        }

        if (EditingProjectId is { } projectId)
        {
            var project = Store.Project(projectId);

            if (project is null || project.IsArchived)
            {
                ValidationMessage = "This project is no longer available. Reload your workspace and try again.";
                NotifyChanged();

                return;
            }

            project.Name = name;
        }
        else
        {
            Store.Data.Projects.Add(new()
            {
                Name = name,
                Color = new[] { "#5b68e8", "#2c9b82", "#df9b39", "#cb6a78" }[ActiveProjectCount % 4]
            });
        }

        DialogOpen = false;
        EditingProjectId = null;
        ActionMessage = "Saving project…";
        NotifyChanged();
        await Store.SaveAsync();
        ActionMessage = Store.SyncMessage ?? "Project saved.";
        NotifyChanged();
    }

    public void RequestArchive(ProjectRecord project)
    {
        ActionMessage = null;

        if (Store.Data.Timer.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused &&
            string.Equals(Store.Data.Timer.ProjectId, project.Id, StringComparison.Ordinal))
        {
            ActionMessage = "Pause or finish this project's timer before archiving it.";
            NotifyChanged();

            return;
        }

        ArchiveProjectId = project.Id;
        NotifyChanged();
    }

    public void CloseArchiveDialog()
    {
        ArchiveProjectId = null;
        NotifyChanged();
    }

    public async Task ConfirmArchiveAsync()
    {
        var project = Store.Project(ArchiveProjectId ?? "");

        if (project is null || project.IsArchived)
        {
            CloseArchiveDialog();

            return;
        }

        if (Store.Data.Timer.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused &&
            string.Equals(Store.Data.Timer.ProjectId, project.Id, StringComparison.Ordinal))
        {
            ArchiveProjectId = null;
            ActionMessage = "Pause or finish this project's timer before archiving it.";
            NotifyChanged();

            return;
        }

        project.IsArchived = true;
        ArchiveProjectId = null;
        ActionMessage = "Saving project…";
        NotifyChanged();
        await Store.SaveAsync();
        ActionMessage = Store.SyncMessage ?? "Project archived. Its tasks and focus history are preserved.";
        NotifyChanged();
    }

    public async Task RestoreProjectAsync(ProjectRecord project)
    {
        ActionMessage = null;

        if (!project.IsArchived)
        {

            return;
        }

        if (ActiveProjectCount >= ProjectLimit)
        {
            ActionMessage = $"Your plan allows {ProjectLimit} active projects. Archive another project or upgrade to restore this one.";
            NotifyChanged();

            return;
        }

        project.IsArchived = false;
        ActionMessage = "Saving project…";
        NotifyChanged();
        await Store.SaveAsync();
        ActionMessage = Store.SyncMessage ?? "Project restored.";
        NotifyChanged();
    }
}
