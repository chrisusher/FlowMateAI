namespace Web.Components.Features.Reports;

public partial class FocusChartCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }


}
