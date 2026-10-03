namespace Web.Components.Features.Settings;

public partial class BillingCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Billing);
    }


}
