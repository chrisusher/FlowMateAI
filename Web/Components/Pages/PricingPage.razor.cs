using Microsoft.AspNetCore.Components;

namespace Web.Components.Pages;

public partial class PricingPage
{
    [SupplyParameterFromQuery(Name = "checkout")]
    public string? CheckoutResult { get; set; }

    [SupplyParameterFromQuery(Name = "annual")]
    public bool? AnnualTrial { get; set; }

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
            "trial-return" => CheckoutOutcome.TrialReturn,
            "trial-cancelled" => CheckoutOutcome.TrialCancelled,
            _ => (CheckoutOutcome?)null
        };

        if (outcome is null || _handledCheckoutOutcome == outcome)
        {
            return;
        }

        _handledCheckoutOutcome = outcome;
        if (outcome is CheckoutOutcome.TrialReturn or CheckoutOutcome.TrialCancelled)
        {
            Billing.Annual = AnnualTrial == true;
        }
        await Billing.InitializeAsync();
        await Billing.ShowCheckoutReturn(outcome.Value);
    }
}
