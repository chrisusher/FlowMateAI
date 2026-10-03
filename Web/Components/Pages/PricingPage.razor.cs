namespace Web.Components.Pages;

public partial class PricingPage
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Billing);
    }
}
