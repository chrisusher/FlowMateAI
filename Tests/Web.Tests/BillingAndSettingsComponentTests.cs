using System.Net;
using System.Net.Http.Json;
using Shared.Contracts;
using Shared.Enums;
using Web.Components.Features.Pricing;
using Web.Components.Features.Settings;
using Web.Components.Pages;

namespace Web.Tests;

public sealed class BillingAndSettingsComponentTests : WorkspaceComponentTest
{
    [Test]
    public void FreePlanCardDisplaysIncludedFeaturesAndDisabledCurrentPlan()
    {
        var cut = Render<FreePlanCard>();
        Assert.That(Content(cut.Find(".price")), Does.Contain("£0"));
        Assert.That(cut.FindAll("li").Count, Is.EqualTo(5));
        Assert.That(cut.Find("button").HasAttribute("disabled"), Is.True);
        Assert.That(Content(cut.Find("button")), Is.EqualTo("Checking plan…"));
    }

    [Test]
    public async Task FreePlanCardDoesNotCallFreeTheCurrentPlanForAProCustomer()
    {
        await SignInAsync();
        Api.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/billing/prices" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<BillingPrice>())
            },
            "/api/v1/billing" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BillingSummary("Pro", "active", false, null, 0, 100))
            },
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        });
        await Billing.InitialiseAsync();

        var cut = Render<FreePlanCard>();

        Assert.That(Content(cut.Find("button")), Is.EqualTo("Included in Pro"));
        Assert.That(cut.Find("button").HasAttribute("disabled"), Is.True);
    }

    [Test]
    public async Task ProPlanCardChangesPriceAndRoutesEntitledCustomersToBillingPortal()
    {
        await SignInAsync();
        await Billing.InitialiseAsync();

        var cut = Render<ProPlanCard>();
        Assert.That(Content(cut.Find(".price")), Is.EqualTo("£10 / month"));
        await cut.InvokeAsync(() => Billing.Annual = true);
        Assert.That(Content(cut.Find(".price")), Is.EqualTo("£96 / year"));
        cut.Find(".checkout-button").Click();
        Assert.That(Billing.BillingMessage, Is.EqualTo("Portal unavailable"));
        Assert.That(cut.Find(".plan-button").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find(".plan-button").TextContent, Does.Contain("Pro subscription active"));
    }

    [TestCase(BillingInterval.Annual, "annual")]
    [TestCase(BillingInterval.Monthly, "monthly")]
    [TestCase(null, "subscription")]
    public async Task TrialRenewalCopyUsesConfirmedIntervalAfterFreshLoadAndIgnoresPricingToggle(BillingInterval? interval, string chargeLabel)
    {
        await SignInAsync();
        Api.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/billing/prices" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<BillingPrice>())
            },
            "/api/v1/billing" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BillingSummary("Pro", "trialing", true, DateTimeOffset.UtcNow.AddDays(10), 0, 100, interval))
            },
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        });

        await Billing.InitialiseAsync();
        Assert.That(Billing.Annual, Is.False);
        var cut = Render<ProPlanCard>();
        Assert.That(cut.Find("p.trial-copy").TextContent, Does.Contain($"avoid the {chargeLabel} charge"));

        await cut.InvokeAsync(() => Billing.Annual = true);
        cut.Render();
        Assert.That(cut.Find("p.trial-copy").TextContent, Does.Contain($"avoid the {chargeLabel} charge"));
    }

    [Test]
    public async Task RepeatedCheckoutClicksSendOnlyOneRequest()
    {
        await SignInAsync();
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkoutRequests = 0;

        Api.Respond = async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/billing/prices")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new BillingPrice("month", "gbp", 1000, "£10 / month") })
                };
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/billing")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new BillingSummary("Free", "none", false, null, 0, 10))
                };
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/billing/checkout")
            {
                Interlocked.Increment(ref checkoutRequests);
                requestStarted.TrySetResult();
                await releaseRequest.Task.WaitAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new BillingActionResponse(false, null, BillingActionCode.StripeError, "Checkout unavailable"))
                };
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        };

        await Billing.InitialiseAsync();
        var firstCheckout = Billing.BuyPro();
        await requestStarted.Task;
        var secondCheckout = Billing.BuyPro();

        Assert.That(Billing.CheckoutInProgress, Is.True);
        Assert.That(checkoutRequests, Is.EqualTo(1));
        releaseRequest.SetResult();
        await Task.WhenAll(firstCheckout, secondCheckout);
        Assert.That(Billing.CheckoutInProgress, Is.False);
    }

    [TestCase(CheckoutOutcome.Return, "active")]
    [TestCase(CheckoutOutcome.TrialReturn, "trialing")]
    public async Task CheckoutReturnSyncsWorkspacePlanWhenServerConfirmsPro(CheckoutOutcome outcome, string confirmedStatus)
    {
        await SignInAsync();
        var plan = BillingPlan.Free;

        Api.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/billing/prices" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<BillingPrice>())
            },
            "/api/v1/billing" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BillingSummary(plan.ToString(), plan == BillingPlan.Pro ? confirmedStatus : "none", false, null, 0, plan == BillingPlan.Pro ? 100 : 10))
            },
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        });

        await Billing.InitialiseAsync();
        await Billing.ShowCheckoutReturn(outcome);
        
        Assert.That(Billing.CheckoutPending, Is.True);
        Assert.That(Store.Data.Plan, Is.EqualTo(BillingPlan.Free));

        await Billing.RefreshSubscriptionStatusAsync();
        Assert.That(Billing.CheckoutPending, Is.True);
        Assert.That(Store.Data.Plan, Is.EqualTo(BillingPlan.Free));

        var previousWrites = WorkspaceModule.Invocations.Count(call => call.Identifier == "write");
        plan = BillingPlan.Pro;
        await Billing.RefreshSubscriptionStatusAsync();
        Assert.That(Billing.CheckoutPending, Is.False);
        Assert.That(Billing.BillingMessage, Is.EqualTo("Your Pro subscription is active."));
        Assert.That(Billing.BillingState?.Status, Is.EqualTo(confirmedStatus));
        Assert.That(Store.Data.Plan, Is.EqualTo(BillingPlan.Pro));
        Assert.That(Projects.ProjectLimit, Is.EqualTo(25));
        Assert.That(WorkspaceModule.Invocations.Count(call => call.Identifier == "write"), Is.GreaterThan(previousWrites));
    }

    [Test]
    public async Task FailedSubscriptionRefreshPreservesWorkspacePlanAndPendingCheckout()
    {
        await SignInAsync();
        await Billing.ShowCheckoutReturn(CheckoutOutcome.TrialReturn);
        var previousWrites = WorkspaceModule.Invocations.Count(call => call.Identifier == "write");
        Api.Respond = (_, _) => throw new HttpRequestException("Offline");

        await Billing.RefreshSubscriptionStatusAsync();

        Assert.That(Billing.CheckoutPending, Is.True);
        Assert.That(Store.Data.Plan, Is.EqualTo(BillingPlan.Free));
        Assert.That(Billing.BillingMessage, Is.EqualTo("Subscription status could not be refreshed. Try again in a moment."));
        Assert.That(WorkspaceModule.Invocations.Count(call => call.Identifier == "write"), Is.EqualTo(previousWrites));
    }

    [Test]
    public async Task ProPlanCardReportsNetworkFailure()
    {
        await SignInAsync();

        Api.Respond = (request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/billing/prices" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[]
                {
                    new BillingPrice("month", "gbp", 1000, "£10 / month"),
                    new BillingPrice("year", "gbp", 9600, "£96 / year")
                })
            }),
            "/api/v1/billing" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BillingSummary("Free", "none", false, null, 0, 10))
            }),
            _ => throw new HttpRequestException("Offline")
        };
        await Billing.InitialiseAsync();

        var cut = Render<ProPlanCard>();
        await cut.InvokeAsync(() => Billing.BuyPro());
        Assert.That(Billing.BillingMessage, Is.EqualTo("Checkout is temporarily unavailable."));
        await cut.InvokeAsync(() => Billing.StartTrial());
        Assert.That(Billing.BillingMessage, Is.EqualTo("Billing is temporarily unavailable."));
    }

    [TestCase("unknown")]
    [TestCase("RETURN")]
    public void PricingPageIgnoresUnknownCheckoutQueryValues(string checkoutResult)
    {
        Navigation.NavigateTo($"pricing?checkout={Uri.EscapeDataString(checkoutResult)}");
        Render<PricingPage>();

        Assert.That(Billing.BillingMessage, Is.Null);
        Assert.That(Api.Paths, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TrialCancellationReturnRestoresSelectedBillingInterval(bool annual)
    {
        Navigation.NavigateTo($"pricing?checkout=trial-cancelled&annual={annual.ToString().ToLowerInvariant()}");

        Render<PricingPage>();

        Assert.That(Billing.Annual, Is.EqualTo(annual));
        Assert.That(Billing.BillingMessage, Does.Contain("Trial checkout was cancelled"));
    }

    [Test]
    public async Task ProfileCardEditsAndSavesProfileAndShowsAuthenticatedEmail()
    {
        await SignInAsync();
        var cut = Render<ProfileCard>();

        Assert.That(cut.FindAll("input")[1].GetAttribute("value"), Is.EqualTo("alex@example.test"));
        Assert.That(cut.FindAll("input")[1].HasAttribute("disabled"), Is.True);

        cut.Find("input").Change("New name");
        cut.Find("select").Change("Asia/Tokyo");
        cut.Find(".quiet-button").Click();

        Assert.That(Store.Data.DisplayName, Is.EqualTo("New name"));
        Assert.That(Store.Data.TimeZone, Is.EqualTo("Asia/Tokyo"));
        Assert.That(WorkspaceModule.Invocations.Any(call => call.Identifier == "write"), Is.True);

        cut.Find(".text-button").Click();
        Assert.That(AuthModule.Invocations.Any(call => call.Identifier == "logout"), Is.True);
    }

    [TestCase("America/Chicago")]
    [TestCase("Asia/Kolkata")]
    [TestCase("Australia/Sydney")]
    [TestCase("UTC")]
    [TestCase("Europe/London")]
    public void ProfileCardDisplaysAndRetainsSavedTimeZoneAfterChangingSelection(string timeZone)
    {
        Store.Data.TimeZone = timeZone;
        var cut = Render<ProfileCard>();

        Assert.That(cut.Find("select").GetAttribute("value"), Is.EqualTo(timeZone));
        Assert.That(cut.FindAll($"option[value='{timeZone}']"), Has.Count.EqualTo(1));

        cut.Find("select").Change("Asia/Tokyo");
        Assert.That(Store.Data.TimeZone, Is.EqualTo("Asia/Tokyo"));
        Assert.That(cut.FindAll($"option[value='{timeZone}']"), Has.Count.EqualTo(1));

        cut.Find("select").Change(timeZone);
        cut.Find(".quiet-button").Click();
        Assert.That(Store.Data.TimeZone, Is.EqualTo(timeZone));
        Assert.That(cut.Find("select").GetAttribute("value"), Is.EqualTo(timeZone));
        Assert.That(WorkspaceModule.Invocations.Any(call => call.Identifier == "write"), Is.True);
    }

    [Test]
    public void PreferencesCardTogglesThemeAndSoundsUsingExistingStorageKey()
    {
        var cut = Render<PreferencesCard>();
        cut.FindAll(".setting-control")[0].Click();

        Assert.That(cut.Markup, Does.Contain("Dark mode on"));
        Assert.That(JSInterop.Invocations.Any(call => call.Identifier == "localStorage.setItem" && Equals(call.Arguments[0], "flowmate.theme") && Equals(call.Arguments[1], "dark")), Is.True);

        Assert.That(JSInterop.Invocations.Any(call => call.Identifier == "eval" && Equals(call.Arguments[0], "document.documentElement.dataset.theme='dark'")), Is.True);

        cut.FindAll(".setting-control")[1].Click();
        Assert.That(Store.Data.Muted, Is.True);
        Assert.That(cut.Markup, Does.Contain("Muted"));
        Assert.That(cut.Markup, Does.Contain("25 minutes"));
    }

    [TestCase("none", false, "Free", "You're on Free")]
    [TestCase("active", false, "Paid", "Next renewal")]
    [TestCase("active", true, "Cancellation scheduled", "Your Pro access ends")]
    [TestCase("trialing", false, "Trial", "Your trial ends")]
    [TestCase("trialing", true, "Trial ending", "Your trial and Pro access are scheduled to end")]
    [TestCase("past_due", false, "Payment past due", "Update your payment method")]
    [TestCase("canceled", false, "Cancelled", "Your Pro subscription has ended")]
    public async Task BillingCardDisplaysConfirmedSubscriptionStatusAndRelevantAction(string status, bool cancelAtPeriodEnd, string label, string detail)
    {
        await SignInAsync();
        Api.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/billing/prices" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<BillingPrice>())
            },
            "/api/v1/billing" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BillingSummary(status is "active" or "trialing" ? "Pro" : "Free", status, false, status == "canceled" ? null : DateTimeOffset.UtcNow.AddDays(5), 0, 10, null, cancelAtPeriodEnd))
            },
            "/api/v1/billing/portal" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        });
        await Billing.InitialiseAsync();
        var cut = Render<BillingCard>();

        Assert.That(Content(cut.Find(".plan-status")), Is.EqualTo(label));
        Assert.That(Content(cut.Find(".billing-status-copy")), Does.Contain(detail));
        Assert.That(cut.Find("article").Id, Is.EqualTo("billing"));

        if (status == "none" || status == "canceled")
        {
            Assert.That(cut.Find("a[href='pricing']").TextContent, Does.Contain("Pro"));
        }
        else
        {
            Assert.That(Content(cut.Find("button")), Is.EqualTo("Manage billing"));
        }
    }

    [Test]
    public async Task BillingCardRefreshShowsAChangedStripeStatus()
    {
        await SignInAsync();
        var status = "active";
        Api.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/billing/prices" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<BillingPrice>())
            },
            "/api/v1/billing" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BillingSummary(status == "active" ? "Pro" : "Free", status, false, DateTimeOffset.UtcNow.AddDays(5), 0, 10))
            },
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        });
        await Billing.InitialiseAsync();
        var cut = Render<BillingCard>();
        Assert.That(Content(cut.Find(".plan-status")), Is.EqualTo("Paid"));

        status = "past_due";
        await Billing.RefreshSubscriptionStatusAsync();
        cut.WaitForAssertion(() => Assert.That(Content(cut.Find(".plan-status")), Is.EqualTo("Payment past due")));
        Assert.That(Content(cut.Find(".billing-status-copy")), Does.Contain("Update your payment method"));

        status = "canceled";
        await Billing.RefreshSubscriptionStatusAsync();
        cut.WaitForAssertion(() => Assert.That(Content(cut.Find(".plan-status")), Is.EqualTo("Cancelled")));
        Assert.That(Content(cut.Find(".billing-status-copy")), Does.Contain("Your existing workspace stays available"));
    }
}
