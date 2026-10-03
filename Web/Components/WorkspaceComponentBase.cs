using Microsoft.AspNetCore.Components;
using Web.Clients;
using Web.Managers;

namespace Web.Components;

public abstract class WorkspaceComponentBase : ComponentBase, IDisposable
{
    private readonly HashSet<WorkspaceManager> _observed = [];
    private bool _disposed;
    [Inject] protected WorkspaceStore Store { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Store.Changed += Refresh;

    protected void Observe(WorkspaceManager manager)
    {
        if (_observed.Add(manager))
        {
            manager.Changed += Refresh;
        }
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _ = InvokeAsync(() =>
        {
            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    protected void Go(string view) => Navigation.NavigateTo(view == "today" ? "" : view);

    public void Dispose()
    {
        _disposed = true;
        Store.Changed -= Refresh;

        foreach (var manager in _observed)
        {
            manager.Changed -= Refresh;
        }

        _observed.Clear();
        GC.SuppressFinalize(this);
    }
}
