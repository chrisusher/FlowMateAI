namespace Web.Components.Features.Today;

public partial class FocusStatsCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }


}
