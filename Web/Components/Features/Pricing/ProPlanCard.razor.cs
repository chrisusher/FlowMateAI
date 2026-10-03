namespace Web.Components.Features.Pricing;

public partial class ProPlanCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Billing);
    }


}
