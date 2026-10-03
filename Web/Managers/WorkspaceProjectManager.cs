using Microsoft.AspNetCore.Components;
using Shared.Enums;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceProjectManager(WorkspaceStore store, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    public string DraftName { get; set; } = "";
    public bool DialogOpen { get; private set; }

    public Task AddProject()
    {
        if (Store.Data.Projects.Count >= (Store.Data.Plan == BillingPlan.Pro ? 25 : 3))
        {
            navigation.NavigateTo("pricing");

            return Task.CompletedTask;
        }
        DraftName = "";
        DialogOpen = true;
        NotifyChanged();

        return Task.CompletedTask;
    }

    public void CloseProjectDialog()
    {
        DialogOpen = false;
        NotifyChanged();
    }

    public async Task SaveProject()
    {
        var name = DraftName.Trim();

        if (name.Length == 0)
        {
            return;
        }

        if (Store.Data.Projects.Count >= (Store.Data.Plan == BillingPlan.Pro ? 25 : 3))
        {
            DialogOpen = false;
            NotifyChanged();
            navigation.NavigateTo("pricing");

            return;
        }
        Store.Data.Projects.Add(new()
        {
            Name = name,
            Color = new[] { "#5b68e8", "#2c9b82", "#df9b39", "#cb6a78" }[Store.Data.Projects.Count % 4]
        });
        DialogOpen = false;
        NotifyChanged();
        await Store.SaveAsync();
    }
}
