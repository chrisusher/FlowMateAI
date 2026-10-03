namespace Web.Components.Features.Today;

public partial class TodayPlanCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
        Observe(Tasks);
    }


}
