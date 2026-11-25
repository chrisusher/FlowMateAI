using Microsoft.AspNetCore.Components;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceProjectManager(WorkspaceStore store, WorkspaceItemsClient items, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;
    private ProjectRecord? _newProject;
    private string? _editingBaseline;
    private string? _archiveBaseline;

    public string DraftName { get; set; } = "";
    public bool DialogOpen { get; private set; }
    public string? EditingProjectId { get; private set; }
    public string? ValidationMessage { get; private set; }
    public string? ActionMessage { get; private set; }
    public string? ArchiveProjectId { get; private set; }
    public bool Saving { get; private set; }
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
        _newProject = new ProjectRecord
        {
            Color = new[] { "#5b68e8", "#2c9b82", "#df9b39", "#cb6a78" }[ActiveProjectCount % 4]
        };
        _editingBaseline = null;
        EditingProjectId = null;
        ValidationMessage = null;
        DialogOpen = true;
        NotifyChanged();

        return Task.CompletedTask;
    }

    public void EditProject(ProjectRecord project)
    {
        DraftName = project.Name;
        _newProject = null;
        _editingBaseline = WorkspaceCollectionManifest.Hash(new[] { project });
        EditingProjectId = project.Id;
        ValidationMessage = null;
        DialogOpen = true;
        NotifyChanged();
    }

    public void CloseProjectDialog()
    {
        if (Saving)
        {
            return;
        }

        DialogOpen = false;
        _newProject = null;
        _editingBaseline = null;
        ValidationMessage = null;
        NotifyChanged();
    }

    public async Task SaveProject()
    {
        if (Saving)
        {
            return;
        }

        var validation = ProjectName.Validate(DraftName, out var name);

        if (validation != ProjectNameValidation.Valid)
        {
            ValidationMessage = validation switch
            {
                ProjectNameValidation.Required => "Enter a project name.",
                ProjectNameValidation.TooLong => "Project names can be up to 80 characters.",
                _ => "Project names cannot contain control characters."
            };
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

        Saving = true;
        ValidationMessage = null;
        ActionMessage = "Saving project…";
        NotifyChanged();

        try
        {
            if (EditingProjectId is { } projectId)
            {
                var baseline = _editingBaseline;
                await Store.WriteProjectAsync(projectId, async () =>
                {
                    var local = Store.Project(projectId);
                    var remote = await items.GetProjectVersionedAsync(projectId);

                    if (local is null || remote is null || local.IsArchived || remote.Project.IsArchived || baseline is null ||
                        WorkspaceCollectionManifest.Hash(new[] { local }) != baseline ||
                        WorkspaceCollectionManifest.Hash(new[] { remote.Project }) != baseline)
                    {
                        throw new InvalidOperationException("This project changed on another device. Reload it before editing.");
                    }

                    return await items.RenameProjectAsync(projectId, new ProjectRenameRequest(name, remote.Revision));
                });
            }
            else
            {
                var candidate = _newProject ?? throw new InvalidOperationException("Open the project editor and try again.");
                var request = new ProjectCreateRequest(candidate.Id, name, candidate.Color, candidate.CreatedAt);
                await Store.WriteProjectAsync(candidate.Id, () => items.CreateProjectAsync(request));
            }

            DialogOpen = false;
            EditingProjectId = null;
            _newProject = null;
            _editingBaseline = null;
            ActionMessage = "Project saved.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or TaskCanceledException)
        {
            ValidationMessage = exception is TaskCanceledException ? "The project request timed out. Try again." : exception.Message;
            ActionMessage = null;
        }
        finally
        {
            Saving = false;
            NotifyChanged();
        }
    }

    public void RequestArchive(ProjectRecord project)
    {
        if (Saving)
        {
            return;
        }

        ActionMessage = null;

        if (TimerReferencesProject(project) && Store.Data.Timer.Phase is (TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused))
        {
            ActionMessage = "Pause or finish this project's timer before archiving it.";
            NotifyChanged();

            return;
        }

        ArchiveProjectId = project.Id;
        _archiveBaseline = WorkspaceCollectionManifest.Hash(new[] { project });
        NotifyChanged();
    }

    public void CloseArchiveDialog()
    {
        if (Saving)
        {
            return;
        }

        ArchiveProjectId = null;
        _archiveBaseline = null;
        NotifyChanged();
    }

    public async Task ConfirmArchiveAsync()
    {
        if (Saving)
        {
            return;
        }

        var project = Store.Project(ArchiveProjectId ?? "");

        if (project is null || project.IsArchived)
        {
            CloseArchiveDialog();

            return;
        }

        if (TimerReferencesProject(project) && Store.Data.Timer.Phase is (TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused))
        {
            ArchiveProjectId = null;
            ActionMessage = "Pause or finish this project's timer before archiving it.";
            NotifyChanged();

            return;
        }

        var baseline = _archiveBaseline;
        Saving = true;
        ActionMessage = "Saving project…";
        NotifyChanged();

        try
        {
            await Store.WriteProjectAsync(project.Id, async () =>
            {
                var local = Store.Project(project.Id);
                var remote = await items.GetProjectVersionedAsync(project.Id);

                if (local is null || remote is null || local.IsArchived || remote.Project.IsArchived || baseline is null ||
                    WorkspaceCollectionManifest.Hash(new[] { local }) != baseline ||
                    WorkspaceCollectionManifest.Hash(new[] { remote.Project }) != baseline)
                {
                    throw new InvalidOperationException("This project changed on another device. Reload it before archiving.");
                }

                var result = await items.ArchiveProjectAsync(project.Id, new ProjectArchiveRequest(remote.Revision, Store.WorkspaceRevision));

                if (string.IsNullOrWhiteSpace(result.WorkspaceRevision) || result.Timer is null)
                {
                    throw new InvalidDataException("The server returned an incomplete archive response. Reload and try again.");
                }

                return result;
            });
            ArchiveProjectId = null;
            _archiveBaseline = null;
            ActionMessage = "Project archived. Its tasks and focus history are preserved.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or TaskCanceledException)
        {
            ActionMessage = exception is TaskCanceledException ? "The project request timed out. Try again." : exception.Message;
        }
        finally
        {
            Saving = false;
            NotifyChanged();
        }
    }

    public async Task RestoreProjectAsync(ProjectRecord project)
    {
        if (Saving)
        {
            return;
        }

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

        var baseline = WorkspaceCollectionManifest.Hash(new[] { project });
        Saving = true;
        ActionMessage = "Saving project…";
        NotifyChanged();

        try
        {
            await Store.WriteProjectAsync(project.Id, async () =>
            {
                var local = Store.Project(project.Id);
                var remote = await items.GetProjectVersionedAsync(project.Id);

                if (local is null || remote is null || !local.IsArchived || !remote.Project.IsArchived ||
                    WorkspaceCollectionManifest.Hash(new[] { local }) != baseline ||
                    WorkspaceCollectionManifest.Hash(new[] { remote.Project }) != baseline)
                {
                    throw new InvalidOperationException("This project changed on another device. Reload it before restoring.");
                }

                return await items.RestoreProjectAsync(project.Id, new ProjectStateRequest(remote.Revision));
            });
            ActionMessage = "Project restored.";
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or InvalidDataException or JsonException or TaskCanceledException)
        {
            ActionMessage = exception is TaskCanceledException ? "The project request timed out. Try again." : exception.Message;
        }
        finally
        {
            Saving = false;
            NotifyChanged();
        }
    }

    private bool TimerReferencesProject(ProjectRecord project)
    {
        var timer = Store.Data.Timer;
        var selectedTask = Store.Task(timer.TaskId);

        return string.Equals(timer.ProjectId, project.Id, StringComparison.Ordinal) ||
            string.Equals(selectedTask?.ProjectId, project.Id, StringComparison.Ordinal);
    }

}
