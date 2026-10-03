using Microsoft.AspNetCore.Components;

namespace Web.Components.Features.Sidebar;

public partial class WorkspaceTopbar
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
        Observe(Appearance);
    }

    [Parameter] public string Title { get; set; } = "Today";
    private string Initials => string.Concat((Auth.Session.Name ?? Store.Data.DisplayName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(n => char.ToUpperInvariant(n[0])));
}
