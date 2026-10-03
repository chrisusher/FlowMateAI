using Microsoft.AspNetCore.Components;

namespace Web.Components.Features.Sidebar;

public partial class WorkspaceSidebar
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }

    [Parameter] public string View { get; set; } = "today";
    private static readonly (string Key, string Label, string Icon)[] NavItems = [("today", "Today", "&#9711;"), ("projects", "Projects", "&#9638;"), ("tasks", "Tasks", "&#9745;"), ("reports", "Reports", "&#9636;"), ("coach", "Focus coach", "&#10024;")];
}
