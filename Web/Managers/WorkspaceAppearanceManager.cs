using Microsoft.JSInterop;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceAppearanceManager(WorkspaceStore store, IJSRuntime js) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    private bool _initialized;
    public bool IsDark { get; private set; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        var savedTheme = await js.InvokeAsync<string?>("localStorage.getItem", "flowmate.theme");
        IsDark = savedTheme == "dark" || (savedTheme is null && await js.InvokeAsync<bool>("eval", "window.matchMedia('(prefers-color-scheme: dark)').matches"));
        await js.InvokeVoidAsync("eval", $"document.documentElement.dataset.theme='{(IsDark ? "dark" : "light")}'");
        _initialized = true;
        NotifyChanged();
    }

    public async Task ToggleMute()
    {
        Store.Data.Muted = !Store.Data.Muted;
        await Store.SaveAsync();
    }

    public async Task ToggleTheme()
    {
        IsDark = !IsDark;
        await js.InvokeVoidAsync("eval", $"document.documentElement.dataset.theme='{(IsDark ? "dark" : "light")}'");
        await js.InvokeVoidAsync("localStorage.setItem", "flowmate.theme", IsDark ? "dark" : "light");
        NotifyChanged();
    }
}
