using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Web.Components.Features.Sidebar;

public partial class WorkspaceSidebar
{
    private bool _profileMenuOpen;

    private void ToggleProfileMenu() => _profileMenuOpen = !_profileMenuOpen;

    private void HandleProfileKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            _profileMenuOpen = false;
        }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }

    [Parameter] public string View { get; set; } = "today";
    private static readonly (string Key, string Label, string Icon)[] NavItems = [("today", "Today", "&#9711;"), ("projects", "Projects", "&#9638;"), ("tasks", "Tasks", "&#9745;"), ("reports", "Reports", "&#9636;"), ("coach", "Focus coach", "&#10024;")];
}
