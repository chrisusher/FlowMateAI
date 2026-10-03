namespace Web.Components.Features.Today;

public partial class TodayProjectsCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }


}
