using Shared.Enums;
using Web.Components.Features.Pricing;
using Web.Components.Features.Settings;

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
    }

    [Test]
    public async Task ProPlanCardChangesPriceAndRoutesEntitledCustomersToBillingPortal()
    {
        await Billing.InitializeAsync();
        var cut = Render<ProPlanCard>();
        Assert.That(Content(cut.Find(".price")), Is.EqualTo("£10 / month"));
        await cut.InvokeAsync(() => Billing.Annual = true);
        Assert.That(Content(cut.Find(".price")), Is.EqualTo("£96 / year"));
        cut.Find(".checkout-button").Click();
        Assert.That(Billing.BillingMessage, Is.EqualTo("Portal unavailable"));
        Assert.That(cut.Find(".plan-button").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find(".plan-button").TextContent, Does.Contain("Pro subscription active"));
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

        await Billing.InitializeAsync();
        var firstCheckout = Billing.BuyPro();
        await requestStarted.Task;
        var secondCheckout = Billing.BuyPro();

        Assert.That(Billing.CheckoutInProgress, Is.True);
        Assert.That(checkoutRequests, Is.EqualTo(1));
        releaseRequest.SetResult();
        await Task.WhenAll(firstCheckout, secondCheckout);
        Assert.That(Billing.CheckoutInProgress, Is.False);
    }

    [Test]
    public async Task CheckoutReturnShowsPendingUntilServerConfirmsPro()
    {
        await SignInAsync();
        var status = "Free";
        Api.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/billing/prices" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<BillingPrice>())
            },
            "/api/v1/billing" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BillingSummary(status, status == "Pro" ? "active" : "none", false, null, 0, 10))
            },
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        });

        await Billing.InitializeAsync();
        Billing.ShowCheckoutReturn("return");
        Assert.That(Billing.CheckoutPending, Is.True);
        Assert.That(Billing.BillingMessage, Does.Contain("Payment is processing"));
        Assert.That(Store.Data.Plan, Is.EqualTo(BillingPlan.Free));

        status = "Pro";
        await Billing.RefreshSubscriptionStatusAsync();
        Assert.That(Billing.CheckoutPending, Is.False);
        Assert.That(Billing.BillingMessage, Is.EqualTo("Your Pro subscription is active."));
    }

    [Test]
    public void ProPlanCardReportsNetworkFailure()
    {
        Api.Respond = (_, _) => throw new HttpRequestException("Offline");
        var cut = Render<ProPlanCard>();
        cut.Find(".checkout-button").Click();
        Assert.That(Billing.BillingMessage, Is.EqualTo("Checkout is temporarily unavailable."));
        cut.Find(".plan-button").Click();
        Assert.That(Billing.BillingMessage, Is.EqualTo("Billing is temporarily unavailable."));
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

    [TestCase(BillingPlan.Free, "3", "10")]
    [TestCase(BillingPlan.Pro, "25", "100")]
    public void BillingCardDisplaysPlanLimitsAndRequestsThePortal(BillingPlan plan, string projects, string prompts)
    {
        Store.Data.Plan = plan;
        var cut = Render<BillingCard>();
        Assert.That(Content(cut.Find(".setting-row")), Does.Contain($"Up to {projects} projects"));
        Assert.That(Content(cut.Find(".setting-row")), Does.Contain($"{prompts} coach prompts"));
        Assert.That(cut.Find("article").Id, Is.EqualTo("billing"));
        cut.Find("button").Click();
        Assert.That(Billing.BillingMessage, Is.EqualTo("Portal unavailable"));
    }
}
