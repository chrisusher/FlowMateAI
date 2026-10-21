using Microsoft.AspNetCore.Components;
using Shared.Enums;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceBillingManager(WorkspaceStore store, BillingClient client, Auth0Client auth, NavigationManager navigation) : WorkspaceManager
{
    private WorkspaceStore Store => store;
    private bool _initialised;
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

    public string? BillingMessage
    {
        get; private set;
    }

    public IReadOnlyList<BillingPrice> Prices { get; private set; } = [];

    public BillingSummary? BillingState
    {
        get; private set;
    }

    public SubscriptionStatus CurrentSubscriptionStatus => SubscriptionStatusExtensions.FromProviderStatus(BillingState?.Status);

    public BillingPlan CurrentPlan => BillingState is null ? BillingPlan.Free : BillingPlanExtensions.ParseOrFree(BillingState.Plan);

    public bool ShouldShowUpgrade => BillingState is not null
        && CurrentPlan == BillingPlan.Free
        && CurrentSubscriptionStatus is (SubscriptionStatus.None or SubscriptionStatus.Canceled or SubscriptionStatus.IncompleteExpired);

    public bool CheckoutInProgress => _checkoutInProgress;
    public bool CheckoutPending => _checkoutPending;
    public string FreePlanButtonText => BillingState is null
        ? "Checking plan…"
        : CurrentPlan == BillingPlan.Free ? "Current plan" : "Included in Pro";

    public BillingPrice? SelectedPrice => FindPrice(Annual ? BillingInterval.Annual : BillingInterval.Monthly);

    public string? AnnualSavingsLabel
    {
        get
        {
            var monthly = FindPrice(BillingInterval.Monthly);
            var annual = FindPrice(BillingInterval.Annual);

            if (monthly is null
                || annual is null
                || monthly.UnitAmount <= 0
                || annual.UnitAmount <= 0
                || !string.Equals(monthly.Currency, annual.Currency, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var monthlyAnnualAmount = monthly.UnitAmount * 12m;
            var saving = (monthlyAnnualAmount - annual.UnitAmount) / monthlyAnnualAmount;
            var roundedPercentage = Math.Round(saving * 100m, 0, MidpointRounding.AwayFromZero);

            return roundedPercentage > 0
                ? $"Save {roundedPercentage:0}%"
                : null;
        }
    }

    public bool HasSelectedPrice => SelectedPrice is not null;
    public bool HasEntitledSubscription => SubscriptionEntitlementExtensions.FromProviderStatus(BillingState?.Status) == SubscriptionEntitlement.Entitled;

    public async Task InitialiseAsync()
    {
        if (_initialised)
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
        _initialised = true;
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

    public string PriceLabel => SelectedPrice?.Display ?? "Pricing unavailable";
    public string RenewalChargeLabel => BillingState?.SubscriptionInterval switch
    {
        BillingInterval.Annual => "annual",
        BillingInterval.Monthly => "monthly",
        _ => "subscription"
    };

    public string SubscriptionStatusLabel => CurrentSubscriptionStatus switch
    {
        SubscriptionStatus.None => "Free",
        SubscriptionStatus.Active when BillingState?.CancelAtPeriodEnd == true => "Cancellation scheduled",
        SubscriptionStatus.Active when BillingState?.IsManualGrant == true => "Preview access",
        SubscriptionStatus.Active => "Paid",
        SubscriptionStatus.Trialing when BillingState?.CancelAtPeriodEnd == true => "Trial ending",
        SubscriptionStatus.Trialing => "Trial",
        SubscriptionStatus.PastDue => "Payment past due",
        SubscriptionStatus.Canceled => "Cancelled",
        SubscriptionStatus.Unpaid => "Payment required",
        SubscriptionStatus.Incomplete => "Payment pending",
        SubscriptionStatus.IncompleteExpired => "Checkout expired",
        SubscriptionStatus.Paused => "Paused",
        _ => "Status unavailable"
    };

    public string SubscriptionStatusDetail => CurrentSubscriptionStatus switch
    {
        SubscriptionStatus.None => "You're on Free. Your existing workspace stays available.",
        SubscriptionStatus.Active when BillingState?.CancelAtPeriodEnd == true => EndDateMessage("Your Pro access ends"),
        SubscriptionStatus.Active when BillingState?.IsManualGrant == true => "Your Pro preview access was granted manually. It has no automatic renewal date.",
        SubscriptionStatus.Active => EndDateMessage("Next renewal"),
        SubscriptionStatus.Trialing when BillingState?.CancelAtPeriodEnd == true => EndDateMessage("Your trial and Pro access are scheduled to end"),
        SubscriptionStatus.Trialing => TrialStatusDetail(),
        SubscriptionStatus.PastDue => "Update your payment method in Manage billing. Your existing work remains available while payment is resolved.",
        SubscriptionStatus.Canceled => "Your Pro subscription has ended. Your existing workspace stays available on Free.",
        SubscriptionStatus.Unpaid => "Payment needs attention. Manage billing to resolve it; your existing work remains available.",
        SubscriptionStatus.Incomplete => "Stripe is still processing your subscription. Refresh the status or manage billing if this takes more than a moment.",
        SubscriptionStatus.IncompleteExpired => "Checkout expired before the subscription was confirmed. You can choose Pro again when you're ready.",
        SubscriptionStatus.Paused => "Your subscription is paused. Manage billing to resume it; your existing work remains available.",
        _ => "Billing status couldn't be identified. Refresh to try again."
    };

    public bool CanManageSubscription => BillingState?.IsManualGrant != true
        && CurrentSubscriptionStatus is (SubscriptionStatus.Active or SubscriptionStatus.Trialing or SubscriptionStatus.PastDue or SubscriptionStatus.Unpaid or SubscriptionStatus.Incomplete or SubscriptionStatus.Paused);

    private string EndDateMessage(string prefix) => BillingState?.PeriodEndsAt is { } end
        ? $"{prefix} {end.ToLocalTime():d}."
        : $"{prefix} date unavailable. Refresh billing status for the latest details.";

    private string TrialStatusDetail() => BillingState?.PeriodEndsAt is { } end
        ? $"Your trial ends {end.ToLocalTime():d}. Unless you cancel first, the {RenewalChargeLabel} charge follows then."
        : "Your trial end date is unavailable. Refresh billing status for the latest details.";

    private BillingPrice? FindPrice(BillingInterval interval)
    {
        var providerInterval = interval switch
        {
            BillingInterval.Monthly => "month",
            BillingInterval.Annual => "year",
            _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, null)
        };

        return Prices.FirstOrDefault(price => string.Equals(price.Interval, providerInterval, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ShowCheckoutReturn(CheckoutOutcome outcome)
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
                BillingMessage = "Checkout was cancelled. No subscription was started.";
                break;
            case CheckoutOutcome.TrialReturn:
                _checkoutPending = !HasEntitledSubscription;
                BillingMessage = _checkoutPending
                    ? "Your trial is being set up. Pro access will appear once Stripe confirms your subscription."
                    : $"Your 14-day Pro trial is active{TrialEndMessage()}. No charge is due until the trial ends.";
                break;
            case CheckoutOutcome.TrialCancelled:
                _checkoutPending = false;
                BillingMessage = "Trial checkout was cancelled. No trial or subscription was started.";
                break;
            default:
                return;
        }

        if ((outcome is CheckoutOutcome.Return or CheckoutOutcome.TrialReturn) && BillingState is not null)
        {
            Store.Data.Plan = BillingPlanExtensions.ParseOrFree(BillingState.Plan);
            await Store.SaveAsync();
        }

        NotifyChanged();
    }

    private string TrialEndMessage() => BillingState?.PeriodEndsAt is { } end
        ? $" until {end.ToLocalTime():d}"
        : "";

    public async Task RefreshSubscriptionStatusAsync()
    {
        try
        {
            BillingState = await client.GetSummaryAsync();

            if (BillingState is not null)
            {
                Store.Data.Plan = BillingPlanExtensions.ParseOrFree(BillingState.Plan);
                await Store.SaveAsync();
            }

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
        if (_checkoutInProgress || !HasSelectedPrice || BillingState?.TrialUsed == true || HasEntitledSubscription)
        {
            return;
        }

        _checkoutInProgress = true;
        NotifyChanged();

        try
        {
            var result = await client.StartTrialAsync(Annual);

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

            BillingMessage = result.Message;
        }
        catch (HttpRequestException)
        {
            BillingMessage = "Billing is temporarily unavailable.";
        }
        finally
        {
            _checkoutInProgress = false;
            NotifyChanged();
        }
    }

    public async Task BuyPro()
    {
        if (_checkoutInProgress)
        {
            return;
        }

        if (CanManageSubscription)
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
