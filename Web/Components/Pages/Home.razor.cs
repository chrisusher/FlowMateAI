using Microsoft.AspNetCore.Components;

namespace Web.Components.Pages;

public partial class Home
{
    [Parameter]
    public string? View
    { get; set; }
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }
}
