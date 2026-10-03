using System.Net;
using System.Net.Http.Json;
using ChrisUsher.Core.Shared;
using Shared.Contracts;
using Web.Components;
using Web.Components.Features.Reports;
using Web.Components.Features.Today;
using Web.Components.Layout;
using Web.Components.Pages;

namespace Web.Tests;

public sealed class WorkspaceRegressionTests : WorkspaceComponentTest
{
    [TestCase(0, TimerPhase.ShortBreak, 300)]
    [TestCase(3, TimerPhase.LongBreak, 900)]
    public async Task FocusExpiryRecordsOnceAndStartsTheCorrectBreakOffTheTodayPage(int completed, TimerPhase nextPhase, int duration)
    {
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Timer.OwnerClientId = "test-device";
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        Store.Data.Timer.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Store.Data.Timer.EndsAt = DateTimeOffset.UtcNow.AddSeconds(1);
        Store.Data.Timer.DurationSeconds = 61;
        Store.Data.Timer.CompletedPomodoros = completed;
        PreserveWorkspaceForInitialization();
        Navigation.NavigateTo("tasks");
        var cut = Render<App>();
        cut.WaitForState(() => Store.Data.Timer.Phase == nextPhase, TimeSpan.FromSeconds(4));
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(nextPhase));
        Assert.That(Content(cut.Find("h1")), Is.EqualTo("All tasks"));
        Assert.That(Store.Data.Timer.DurationSeconds, Is.EqualTo(duration));
        Assert.That(Store.Data.Timer.CompletedPomodoros, Is.EqualTo(completed + 1));
        Assert.That(Store.Data.Sessions.Single().FocusMinutes, Is.EqualTo(1));
        await Timer.StopClockAsync();
        Assert.That(Store.Data.Sessions, Has.Exactly(1).Items);
    }

    [Test]
    public async Task BreakExpiryStartsFocusWithoutRecordingAnotherSession()
    {
        Store.Data.Timer.Phase = TimerPhase.ShortBreak;
        Store.Data.Timer.OwnerClientId = "test-device";
        Store.Data.Timer.StartedAt = DateTimeOffset.UtcNow;
        Store.Data.Timer.EndsAt = DateTimeOffset.UtcNow.AddSeconds(1);
        Store.Data.Timer.DurationSeconds = 1;
        PreserveWorkspaceForInitialization();
        var cut = Render<WorkspaceLayout>();
        cut.WaitForState(() => Store.Data.Timer.Phase == TimerPhase.Focus, TimeSpan.FromSeconds(4));
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.Data.Timer.DurationSeconds, Is.EqualTo(1500));
        Assert.That(Store.Data.Sessions, Is.Empty);
        await Timer.StopClockAsync();
    }

    [Test]
    public async Task FocusStatsCardUsesTheLatestSnapshotAfterASaveConflict()
    {
        await SignInAsync();
        var cut = Render<FocusStatsCard>();
        var latest = new WorkspaceSnapshot
        {
            DisplayName = "Latest workspace",
            Projects = [Project],
            Tasks = [PlannedTask],
            Sessions = [new() { ProjectId = Project.Id, StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow.AddMinutes(60), FocusMinutes = 60 }]
        };
        Api.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(latest, "latest-revision"), options: SharedCommon.JsonOptions)
            });
        await cut.InvokeAsync(Store.SaveAsync);
        cut.WaitForState(() => cut.FindAll(".focus-total strong").Any(element => Content(element) == "60m"));
        Assert.That(Content(cut.Find(".focus-total strong")), Is.EqualTo("60m"));
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Latest workspace"));
    }

    [Test]
    public async Task DisposedCardsStopRenderingWhileLiveCardsStillUpdate()
    {
        var disposed = Render<FocusTimeCard>();
        var live = Render<FocusTimeCard>();
        disposed.Dispose();
        var renderCount = disposed.RenderCount;
        AddSession(25);
        await live.InvokeAsync(() => Stats.Period = ReportPeriod.Day);
        await live.InvokeAsync(Store.SaveAsync);
        live.WaitForState(() => live.FindAll("strong").Any(element => Content(element) == "25 min"));
        Assert.That(Content(live.Find("strong")), Is.EqualTo("25 min"));
        Assert.That(disposed.RenderCount, Is.EqualTo(renderCount));
    }

    [Test]
    public void FocusTimerCardWithAnEmptyPlanNavigatesToTasksWhenChangingTask()
    {
        Store.Data.Tasks.Clear();
        var cut = Render<FocusTimerCard>();
        cut.Find(".session-note button").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/tasks"));
    }

    [Test]
    public void PricingPageRetainsTheBillingIntervalWhenRecreated()
    {
        var first = Render<PricingPage>();
        first.FindAll(".billing-toggle button")[1].Click();
        first.Dispose();
        var second = Render<PricingPage>();
        Assert.That(second.FindAll(".billing-toggle button")[1].ClassName, Does.Contain("selected"));
        Assert.That(second.Find(".checkout-button").TextContent, Does.Contain("Subscribe annually"));
    }

    [Test]
    public void RoutesPreserveSettingsHashAndQueryWhileSelectingTheCorrectTitle()
    {
        PreserveWorkspaceForInitialization();
        Navigation.NavigateTo("settings?source=profile#billing");
        var cut = Render<Routes>();
        cut.WaitForState(() => cut.FindAll(".crumb strong").Any(element => Content(element) == "Settings"));
        Assert.That(Content(cut.Find(".crumb strong")), Is.EqualTo("Settings"));
        Assert.That(cut.FindAll("#billing"), Has.Exactly(1).Items);
    }
}
