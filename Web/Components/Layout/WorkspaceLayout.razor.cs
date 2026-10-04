using Microsoft.AspNetCore.Components.Routing;

namespace Web.Components.Layout;

public partial class WorkspaceLayout
{
    private bool _ready;
    private bool _disposed;
    private string CurrentView
    {
        get
        {
            var path = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].Trim('/').ToLowerInvariant();

            return path is "projects" or "tasks" or "reports" or "coach" or "pricing" or "settings" ? path : "today";
        }
    }
    private string Title => CurrentView switch
    {
        "projects" => "Projects",
        "tasks" => "Tasks",
        "reports" => "Reports",
        "coach" => "Focus coach",
        "pricing" => "Plans",
        "settings" => "Settings",
        _ => "Today"
    };

    protected override async Task OnInitializedAsync()
    {
        Navigation.LocationChanged += OnLocationChanged;
        Store.Changed += OnWorkspaceChanged;
        await Auth.InitialiseAsync();
        await Store.InitialiseAsync();
        Coach.Initialize();
        await Billing.InitializeAsync();
        await Appearance.InitializeAsync();

        if (_disposed)
        {
            return;
        }

        _ready = true;
        Timer.StartClock();
    }

    private void OnWorkspaceChanged()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        if (!_disposed)
        {
            _ = InvokeAsync(() =>
            {
                if (!_disposed)
                {
                    StateHasChanged();
                }
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Navigation.LocationChanged -= OnLocationChanged;
        Store.Changed -= OnWorkspaceChanged;
        await Timer.StopClockAsync();
        GC.SuppressFinalize(this);
    }
}
