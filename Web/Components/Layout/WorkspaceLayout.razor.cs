using Microsoft.AspNetCore.Components.Routing;

namespace Web.Components.Layout;

public partial class WorkspaceLayout
{
    private bool _ready;
    private bool _disposed;
    private CancellationTokenSource? _sessionMonitorCancellation;
    private Task? _sessionMonitorTask;
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
        Auth.SessionChanged += OnSessionChanged;
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
        _sessionMonitorCancellation = new CancellationTokenSource();
        _sessionMonitorTask = MonitorSessionExpirationAsync(_sessionMonitorCancellation.Token);
    }

    private async Task MonitorSessionExpirationAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                Auth.CheckSessionExpiration();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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

    private void OnSessionChanged()
    {
        if (!_disposed)
        {
            _ = HandleSessionChangeAsync();
        }
    }

    private async Task HandleSessionChangeAsync()
    {
        if (Auth.IsSessionExpired)
        {
            await Store.PreserveForReauthenticationAsync();
        }

        if (!_disposed)
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Navigation.LocationChanged -= OnLocationChanged;
        Store.Changed -= OnWorkspaceChanged;
        Auth.SessionChanged -= OnSessionChanged;

        if (_sessionMonitorCancellation is not null)
        {
            await _sessionMonitorCancellation.CancelAsync();

            if (_sessionMonitorTask is not null)
            {
                await _sessionMonitorTask;
            }

            _sessionMonitorCancellation.Dispose();
        }

        await Timer.StopClockAsync();
        GC.SuppressFinalize(this);
    }
}
