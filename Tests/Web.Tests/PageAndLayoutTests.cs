using Microsoft.AspNetCore.Components;
using Shared.Enums;
using Web.Clients;
using Web.Components;
using Web.Components.Layout;
using Web.Components.Pages;

namespace Web.Tests;

public sealed class PageAndLayoutTests : WorkspaceComponentTest
{
    [Test]
    public void HomeComposesAllFiveTodayCardsAndNavigatesToTasks()
    {
        var cut = Render<Home>();
        Assert.That(Content(cut.Find("h1")), Is.EqualTo("Make today count."));
        Assert.That(cut.FindAll(".today-grid article").Count, Is.EqualTo(5));
        cut.Find(".page-heading button").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/tasks"));
    }

    [Test]
    public void ProjectsPageCreatesProjectsAndReactsToTheWorkspace()
    {
        var cut = Render<ProjectsPage>();
        Assert.That(cut.FindAll(".project-card"), Has.Exactly(1).Items);
        cut.Find(".page-heading button").Click();
        Assert.That(Projects.DialogOpen, Is.True);
    }

    [Test]
    public void TasksPageOpensTheTaskEditor()
    {
        var cut = Render<TasksPage>();
        Assert.That(cut.FindAll(".task-row").Count, Is.EqualTo(2));
        cut.Find(".page-heading button").Click();
        Assert.That(Tasks.DialogOpen, Is.True);
    }

    [Test]
    public void ReportsPageSharesTheSelectedPeriodWithItsCards()
    {
        var cut = Render<ReportsPage>();
        Assert.That(cut.FindAll(".report-summary article").Count, Is.EqualTo(3));
        cut.Find("select").Change("Day");
        Assert.That(Stats.Period, Is.EqualTo(ReportPeriod.Day));
        Assert.That(cut.FindAll(".bar-column"), Has.Exactly(1).Items);
        Assert.That(cut.FindAll(".history-note"), Has.Exactly(1).Items);
    }

    [Test]
    public void CoachPageCreatesANewConversation()
    {
        var cut = Render<CoachPage>();
        cut.Find(".page-heading button").Click();
        Assert.That(Store.Data.Conversations.Count, Is.EqualTo(2));
        Assert.That(Coach.ActiveConversation.Title, Is.EqualTo("A fresh perspective"));
        Assert.That(Coach.ChatMessages, Is.Empty);
    }

    [Test]
    public async Task PricingPageChangesIntervalAndShowsActionMessages()
    {
        await Billing.InitializeAsync();
        var cut = Render<PricingPage>();
        cut.FindAll(".billing-toggle button")[1].Click();
        Assert.That(Billing.Annual, Is.True);
        Assert.That(Content(cut.Find(".pro-card .price")), Is.EqualTo("£96 / year"));
        cut.Find(".checkout-button").Click();
        Assert.That(Content(cut.Find(".billing-message")), Is.EqualTo("Checkout unavailable"));
    }

    [Test]
    public void SettingsPageComposesTheThreeSettingsCardsAndAnchorLinks()
    {
        var cut = Render<SettingsPage>();
        Assert.That(cut.FindAll(".settings-panel").Count, Is.EqualTo(3));
        Assert.That(cut.FindAll(".settings-nav a")[1].GetAttribute("href"), Is.EqualTo("settings#preferences"));
        Assert.That(cut.Find("#preferences").Id, Is.EqualTo("preferences"));
        Assert.That(cut.Find("#billing").Id, Is.EqualTo("billing"));
    }

    [TestCase("/", "Make today count.", "Today")]
    [TestCase("/today", "Make today count.", "Today")]
    [TestCase("/projects", "Projects", "Projects")]
    [TestCase("/tasks", "All tasks", "Tasks")]
    [TestCase("/reports", "Focus reports", "Reports")]
    [TestCase("/coach", "Focus coach", "Focus coach")]
    [TestCase("/pricing", "More space to focus.", "Plans")]
    [TestCase("/settings", "Settings", "Settings")]
    [TestCase("/PROJECTS", "Projects", "Projects")]
    [TestCase("/unknown", "Make today count.", "Today")]
    public void RoutesResolveEachPageAndKeepTheWorkspaceLayout(string path, string heading, string title)
    {
        PreserveWorkspaceForInitialization();
        Navigation.NavigateTo(path);
        var cut = Render<Routes>();
        cut.WaitForState(() => cut.FindAll("h1").Any(element => Content(element) == heading));
        Assert.That(Content(cut.Find("h1")), Is.EqualTo(heading));
        Assert.That(Content(cut.Find(".crumb strong")), Is.EqualTo(title));
        Assert.That(cut.FindAll("#blazor-error-ui"), Has.Exactly(1).Items);
    }

    [Test]
    public async Task AppKeepsTheLayoutTimerAndReportSelectionAcrossNavigation()
    {
        PreserveWorkspaceForInitialization();
        var cut = Render<App>();
        cut.WaitForElement(".timer-primary").Click();
        var layout = cut.FindComponent<WorkspaceLayout>().Instance;
        var startedAt = Store.Data.Timer.StartedAt;
        await cut.InvokeAsync(() => Navigation.NavigateTo("reports"));
        cut.WaitForElement("select").Change("Month");
        await cut.InvokeAsync(() => Navigation.NavigateTo("tasks"));
        await cut.InvokeAsync(() => Navigation.NavigateTo("reports"));
        Assert.That(Stats.Period, Is.EqualTo(ReportPeriod.Month));
        Assert.That(cut.FindComponent<WorkspaceLayout>().Instance, Is.SameAs(layout));
        Assert.That(Store.Data.Timer.StartedAt, Is.EqualTo(startedAt));
        Assert.That(WorkspaceModule.Invocations.Count(call => call.Identifier == "getClientId"), Is.EqualTo(1));
    }

    [Test]
    public void MainLayoutPreservesItsBodyAndErrorUi()
    {
        var cut = Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "Workspace body"))));
        Assert.That(cut.Markup, Does.Contain("Workspace body"));
        Assert.That(cut.Find("#blazor-error-ui a").ClassName, Is.EqualTo("reload"));
    }

    [Test]
    public void ErrorPageUsesTheOuterLayoutWithoutTheWorkspace()
    {
        Navigation.NavigateTo("Error");
        var cut = Render<Routes>();
        Assert.That(Content(cut.Find("h1")), Is.EqualTo("Error."));
        Assert.That(cut.FindAll(".app-frame"), Is.Empty);
        Assert.That(cut.FindAll("#blazor-error-ui"), Has.Exactly(1).Items);
    }

    [Test]
    public void NavMenuRendersItsExistingLinksAndImportsItsModule()
    {
        var cut = Render<NavMenu>();
        Assert.That(Content(cut.Find(".navbar-brand")), Is.EqualTo("FlowMate AI"));
        Assert.That(cut.FindAll("nav a").Select(a => a.GetAttribute("href")), Is.EqualTo(new[] { "", "counter", "weather" }));
        Assert.That(JSInterop.Invocations.Any(call => call.Identifier == "import" && Equals(call.Arguments[0], "./Components/Layout/NavMenu.razor.js")), Is.True);
    }

    [Test]
    public void WorkspaceLayoutShowsLoadingUntilInitializationCompletes()
    {
        var initialization = AuthModule.Setup<Web.Clients.AuthSession>("initialize", _ => true);
        var cut = Render<WorkspaceLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "Loaded body"))));
        Assert.That(cut.FindAll("[role=status]"), Has.Exactly(1).Items);
        Assert.That(cut.Markup, Does.Not.Contain("Loaded body"));
        initialization.SetResult(new());
        cut.WaitForState(() => cut.Markup.Contains("Loaded body"));
        Assert.That(cut.Markup, Does.Contain("Loaded body"));
        Assert.That(cut.FindAll("[role=status]"), Has.Exactly(1).Items);
        Assert.That(cut.Markup, Does.Contain("Saved on this device. Sign in to sync this workspace."));
    }

    [Test]
    public void ExpiredSessionKeepsTheCurrentPageMountedAndOffersReauthentication()
    {
        Configuration["Auth0:Domain"] = "auth.example.test";
        Configuration["Auth0:ClientId"] = "test-client";
        Configuration["Auth0:Audience"] = "test-api";
        AuthModule.Setup<AuthSession>("initialize", _ => true).SetResult(new()
        {
            Configured = true,
            SignedIn = true,
            Sub = "test-user",
            AccessToken = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds()
        });
        var cut = Render<WorkspaceLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "Draft editor stays mounted"))));

        cut.WaitForState(() => cut.Markup.Contains("Draft editor stays mounted"));

        Assert.That(Auth.IsSessionExpired, Is.True);
        Assert.That(cut.Markup, Does.Contain("Your session expired."));
        Assert.That(cut.Markup, Does.Contain("Draft editor stays mounted"));
        Assert.That(cut.FindAll(".workspace-session-expired button"), Has.Exactly(1).Items);
    }

    [Test]
    public void IdleExpiryShowsReauthenticationAndKeepsTheCurrentPageMounted()
    {
        Configuration["Auth0:Domain"] = "auth.example.test";
        Configuration["Auth0:ClientId"] = "test-client";
        Configuration["Auth0:Audience"] = "test-api";
        AuthModule.Setup<AuthSession>("initialize", _ => true).SetResult(new()
        {
            Configured = true,
            SignedIn = true,
            Sub = "test-user",
            AccessToken = "short-lived-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(2).ToUnixTimeMilliseconds()
        });
        var cut = Render<WorkspaceLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "Idle draft remains visible"))));
        cut.WaitForState(() => cut.Markup.Contains("Idle draft remains visible"));

        cut.WaitForState(() => Auth.IsSessionExpired, TimeSpan.FromSeconds(8));

        Assert.That(cut.Markup, Does.Contain("Your session expired."));
        Assert.That(cut.Markup, Does.Contain("Idle draft remains visible"));
    }

    [Test]
    public void WorkspaceLayoutGatesItsBodyWhenSignInIsRequired()
    {
        Configuration["Auth0:Domain"] = "auth.example.test";
        Configuration["Auth0:ClientId"] = "client";
        Configuration["Auth0:Audience"] = "api";
        var cut = Render<WorkspaceLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "Private body"))));
        cut.WaitForElement(".auth-screen");
        Assert.That(cut.Markup, Does.Not.Contain("Private body"));
        Assert.That(cut.FindAll(".auth-provider").Count, Is.EqualTo(4));
    }

    [Test]
    public async Task WorkspaceLayoutDisposalStopsTheClock()
    {
        PreserveWorkspaceForInitialization();
        var cut = Render<WorkspaceLayout>();
        cut.WaitForElement(".content-area");
        await DisposeComponentsAsync();
        var ticks = 0;
        Timer.Changed += () => ticks++;
        await Timer.StartFocus();
        await Task.Delay(1200, NUnit.Framework.TestContext.CurrentContext.CancellationToken);
        Assert.That(ticks, Is.EqualTo(0));
    }
}
