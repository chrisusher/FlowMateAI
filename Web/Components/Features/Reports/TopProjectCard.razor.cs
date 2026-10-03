namespace Web.Components.Features.Reports;

public partial class TopProjectCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }


}
