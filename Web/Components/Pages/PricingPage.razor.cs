using Microsoft.AspNetCore.Components;
using Shared.Enums;

namespace Web.Components.Pages;

public partial class PricingPage
{
    [SupplyParameterFromQuery(Name = "checkout")]
    public string? CheckoutResult { get; set; }

    private CheckoutOutcome? _handledCheckoutOutcome;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Billing);
    }

    protected override async Task OnParametersSetAsync()
    {
        var outcome = CheckoutResult switch
        {
            "return" => CheckoutOutcome.Return,
            "cancelled" => CheckoutOutcome.Cancelled,
            _ => (CheckoutOutcome?)null
        };

        if (outcome is null || _handledCheckoutOutcome == outcome)
        {
            return;
        }

        _handledCheckoutOutcome = outcome;
        await Billing.InitializeAsync();
        Billing.ShowCheckoutReturn(outcome.Value);
    }
}
