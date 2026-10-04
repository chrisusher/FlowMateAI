using Web.Components.Features.Account;
using Web.Components.Features.Sidebar;

namespace Web.Tests;

public sealed class SidebarAndAccountComponentTests : WorkspaceComponentTest
{
    [Test]
    public void UpgradeCardLinksToPricing()
    {
        var cut = Render<UpgradeCard>();
        Assert.That(cut.Markup, Does.Contain("Make room for more"));
        Assert.That(cut.Find("a").GetAttribute("href"), Is.EqualTo("pricing"));
    }

    [Test]
    public async Task WorkspaceSidebarShowsActiveViewAndLivePlannedCount()
    {
        var cut = Render<WorkspaceSidebar>(p => p.Add(c => c.View, "projects"));
        Assert.That(cut.Find(".side-link.active").GetAttribute("aria-label"), Is.EqualTo("Projects"));
        Assert.That(cut.Find(".side-link.active").GetAttribute("aria-current"), Is.EqualTo("page"));
        Assert.That(Content(cut.Find(".nav-count")), Is.EqualTo("1"));
        PlannedTask.PlannedToday = false;
        await cut.InvokeAsync(Store.SaveAsync);
        cut.WaitForState(() => cut.FindAll(".nav-count").Count == 0);
        Assert.That(cut.FindAll(".nav-count"), Is.Empty);
        Assert.That(cut.Find(".profile-link").GetAttribute("href"), Is.EqualTo("settings"));
        Assert.That(Content(cut.Find(".brand")), Does.Contain("flowmate.ai"));
    }

    [Test]
    public void WorkspaceTopbarShowsTitleAndAccountAndTogglesTheme()
    {
        var cut = Render<WorkspaceTopbar>(p => p.Add(c => c.Title, "Reports"));
        Assert.That(Content(cut.Find(".crumb strong")), Is.EqualTo("Reports"));
        Assert.That(Content(cut.Find(".user-chip")), Is.EqualTo("AM"));
        cut.Find(".icon-button").Click();
        Assert.That(cut.Find(".icon-button").GetAttribute("aria-label"), Is.EqualTo("Switch to light mode"));
        cut.Find(".user-chip").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/settings"));
    }

    [Test]
    public void WorkspaceTopbarShowsSignInForAConfiguredSignedOutUser()
    {
        Configuration["Keycloak:Url"] = "auth.example.test";
        Configuration["Keycloak:ClientId"] = "client";
        Configuration["Keycloak:Realm"] = "api";
        var cut = Render<WorkspaceTopbar>();
        Assert.That(cut.FindAll(".user-chip"), Is.Empty);
        Assert.That(Content(cut.Find(".quiet-button")), Is.EqualTo("Sign in"));
        cut.Find(".quiet-button").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/settings"));
    }

    [TestCase(0, "google")]
    [TestCase(1, "github")]
    [TestCase(2, "microsoft-personal")]
    [TestCase(3, "microsoft-work-school")]
    public void WorkspaceSignInUsesTheSelectedProvider(int button, string connection)
    {
        var cut = Render<WorkspaceSignIn>();
        Assert.That(cut.FindAll(".auth-provider").Count, Is.EqualTo(4));
        cut.FindAll(".auth-provider")[button].Click();
        var invocation = AuthModule.Invocations.Where(call => call.Identifier == "login").Single();
        Assert.That(invocation.Arguments[0], Is.EqualTo(connection));
    }
}
