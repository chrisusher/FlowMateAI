using Microsoft.AspNetCore.Components;
using Shared.Contracts;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceBillingManager(WorkspaceStore store, BillingClient client, Auth0Client auth, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    private bool _initialized;
    private bool _annual;
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

    public string PriceLabel => Prices.FirstOrDefault(p => p.Interval == (Annual ? "year" : "month"))?.Display ?? "Price set in Stripe";

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
            BillingState = await client.GetSummaryAsync();
            Store.Data.Plan = BillingState?.Plan ?? "Free";
            await Store.SaveAsync();
            BillingMessage = result.Message;
        }
        catch (HttpRequestException) { BillingMessage = "Billing is temporarily unavailable."; }
        finally { NotifyChanged(); }
    }

    public async Task BuyPro()
    {
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
        finally { NotifyChanged(); }
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
