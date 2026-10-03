namespace Web.Components.Features.Reports;

public partial class SessionCountCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Stats);
    }


}
