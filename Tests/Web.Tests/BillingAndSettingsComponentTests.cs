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
    public async Task ProPlanCardChangesPriceAndHandlesTrialAndCheckout()
    {
        await Billing.InitializeAsync();
        var cut = Render<ProPlanCard>();
        Assert.That(Content(cut.Find(".price")), Is.EqualTo("£10 / month"));
        await cut.InvokeAsync(() => Billing.Annual = true);
        Assert.That(Content(cut.Find(".price")), Is.EqualTo("£96 / year"));
        cut.Find(".checkout-button").Click();
        Assert.That(Billing.BillingMessage, Is.EqualTo("Checkout unavailable"));
        cut.Find(".plan-button").Click();
        Assert.That(Store.Data.Plan, Is.EqualTo("Pro"));
        Assert.That(Billing.BillingMessage, Is.EqualTo("Trial started"));
        Assert.That(cut.Find(".plan-button").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find(".plan-button").TextContent, Does.Contain("Trial already used"));
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
