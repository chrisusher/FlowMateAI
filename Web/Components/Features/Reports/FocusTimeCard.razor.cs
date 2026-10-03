namespace Web.Components.Features.Reports;

public partial class FocusTimeCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }


}
