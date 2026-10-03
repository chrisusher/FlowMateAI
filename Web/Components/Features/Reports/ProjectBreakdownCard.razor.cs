namespace Web.Components.Features.Reports;

public partial class ProjectBreakdownCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }


}
