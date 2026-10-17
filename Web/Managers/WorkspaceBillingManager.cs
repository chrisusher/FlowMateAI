using Microsoft.AspNetCore.Components;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceBillingManager(WorkspaceStore store, BillingClient client, Auth0Client auth, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    private bool _initialized;
    private bool _annual;
    private bool _checkoutInProgress;
    private bool _checkoutPending;
    public bool Annual
    {
        get => _annual;
        set
        {
            if (_annual == value)
            {
                return;
            }

            _annual = value;
            NotifyChanged();
        }
    }
    public string? BillingMessage { get; private set; }
    public IReadOnlyList<BillingPrice> Prices { get; private set; } = [];
    public BillingSummary? BillingState { get; private set; }
    public bool CheckoutInProgress => _checkoutInProgress;
    public bool CheckoutPending => _checkoutPending;
    public bool HasSelectedPrice => Prices.Any(price => price.Interval == (Annual ? "year" : "month"));
    public bool HasEntitledSubscription => SubscriptionEntitlementExtensions.FromProviderStatus(BillingState?.Status) == SubscriptionEntitlement.Entitled;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            Prices = await client.GetPricesAsync();
        }
        catch (HttpRequestException) { }

        if (auth.Session.SignedIn)
        {
            try
            {
                BillingState = await client.GetSummaryAsync();
            }
            catch (HttpRequestException) { }
        }
        _initialized = true;
        NotifyChanged();
    }

    public void UpdatePromptUsage(int promptsUsed, int promptLimit)
    {
        BillingState = BillingState is null ? null : BillingState with
        {
            PromptsUsed = promptsUsed,
            PromptLimit = promptLimit
        };
        NotifyChanged();
    }

    public string PriceLabel => Prices.FirstOrDefault(p => p.Interval == (Annual ? "year" : "month"))?.Display ?? "Pricing unavailable";

    public void ShowCheckoutReturn(CheckoutOutcome outcome)
    {
        switch (outcome)
        {
            case CheckoutOutcome.Return:
                _checkoutPending = !HasEntitledSubscription;
                BillingMessage = _checkoutPending
                    ? "Payment is processing. Pro access will appear once Stripe confirms your subscription."
                    : "Your Pro subscription is active.";
                break;
            case CheckoutOutcome.Cancelled:
                _checkoutPending = false;
                BillingMessage = "Checkout was canceled. No subscription was started.";
                break;
            default:
                return;
        }

        NotifyChanged();
    }

    public async Task RefreshSubscriptionStatusAsync()
    {
        try
        {
            BillingState = await client.GetSummaryAsync();
            _checkoutPending = !HasEntitledSubscription;
            BillingMessage = _checkoutPending
                ? "Payment is still processing. Try checking again in a moment."
                : "Your Pro subscription is active.";
        }
        catch (HttpRequestException)
        {
            BillingMessage = "Subscription status could not be refreshed. Try again in a moment.";
        }
        finally
        {
            NotifyChanged();
        }
    }

    public async Task StartTrial()
    {
        try
        {
            var result = await client.StartTrialAsync();

            if (!result.Succeeded)
            {
                BillingMessage = result.Message;

                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Url))
            {
                navigation.NavigateTo(result.Url, forceLoad: true);

                return;
            }

            BillingState = await client.GetSummaryAsync();
            Store.Data.Plan = BillingPlanExtensions.ParseOrFree(BillingState?.Plan);
            await Store.SaveAsync();
            BillingMessage = result.Message;
        }
        catch (HttpRequestException) { BillingMessage = "Billing is temporarily unavailable."; }
        finally { NotifyChanged(); }
    }

    public async Task BuyPro()
    {
        if (_checkoutInProgress)
        {
            return;
        }

        if (HasEntitledSubscription)
        {
            await OpenBillingPortal();

            return;
        }

        if (!HasSelectedPrice)
        {
            return;
        }

        _checkoutInProgress = true;
        NotifyChanged();

        try
        {
            var result = await client.CheckoutAsync(Annual);

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Url))
            {
                navigation.NavigateTo(result.Url, forceLoad: true);
            }
            else
            {
                BillingMessage = result.Message;
            }
        }
        catch (HttpRequestException) { BillingMessage = "Checkout is temporarily unavailable."; }
        finally
        {
            _checkoutInProgress = false;
            NotifyChanged();
        }
    }

    public async Task OpenBillingPortal()
    {
        try
        {
            var result = await client.PortalAsync();

            if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Url))
            {
                navigation.NavigateTo(result.Url, forceLoad: true);
            }
            else
            {
                BillingMessage = result.Message;
            }
        }
        catch (HttpRequestException) { BillingMessage = "Billing is temporarily unavailable."; }
        finally { NotifyChanged(); }
    }
}
