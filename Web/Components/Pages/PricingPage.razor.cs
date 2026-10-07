using Microsoft.AspNetCore.Components;

namespace Web.Components.Pages;

public partial class PricingPage
{
    [SupplyParameterFromQuery(Name = "checkout")]
    public string? CheckoutResult { get; set; }

    private string? _handledCheckoutResult;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Billing);
    }

    protected override async Task OnParametersSetAsync()
    {
        if ((CheckoutResult is "return" or "cancelled") && _handledCheckoutResult != CheckoutResult)
        {
            _handledCheckoutResult = CheckoutResult;
            await Billing.InitializeAsync();
            Billing.ShowCheckoutReturn(CheckoutResult);
        }
    }
}
