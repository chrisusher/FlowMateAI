namespace Web.Components.Pages;

public partial class ReportsPage
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }
}
