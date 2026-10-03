using Web.Components.Features.Today;

namespace Web.Tests;

public sealed class TodayComponentTests : WorkspaceComponentTest
{
    [Test]
    public void FocusTimerCardStartsPausesResumesAndFinishesFocus()
    {
        var cut = Render<FocusTimerCard>();
        cut.Find(".timer-primary").Click();
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.Data.Timer.TaskId, Is.EqualTo(PlannedTask.Id));
        Assert.That(cut.Find(".timer-digits").TextContent, Does.Contain("25:00"));
        cut.Find(".timer-primary").Click();
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Paused));
        Assert.That(cut.Find(".timer-primary").TextContent, Does.Contain("Resume focus"));
        cut.Find(".timer-primary").Click();
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        cut.Find(".timer-secondary").Click();
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Idle));
        Assert.That(cut.Find(".timer-primary").TextContent, Does.Contain("Start focus"));
    }

    [TestCase(TimerPhase.Focus, "Timer active on another device")]
    [TestCase(TimerPhase.Paused, "Timer paused on another device")]
    [TestCase(TimerPhase.ShortBreak, "Timer active on another device")]
    public void FocusTimerCardHidesControlsForAnotherDevice(TimerPhase phase, string message)
    {
        Store.Data.Timer.Phase = phase;
        Store.Data.Timer.OwnerClientId = "other-device";
        var cut = Render<FocusTimerCard>();
        Assert.That(cut.Find(".timer-other-device").TextContent, Does.Contain(message));
        Assert.That(cut.FindAll(".timer-primary,.timer-secondary"), Is.Empty);
    }

    [Test]
    public void FocusTimerCardCanStartAfterRecoveryFromAnotherDevice()
    {
        Store.Data.Timer.OwnerClientId = "other-device";
        var cut = Render<FocusTimerCard>();
        cut.Find(".timer-primary").Click();
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.Data.Timer.OwnerClientId, Is.EqualTo(Store.ClientId));
    }

    [TestCase(TimerPhase.ShortBreak)]
    [TestCase(TimerPhase.LongBreak)]
    public void FocusTimerCardSkipsBreakAndTogglesSounds(TimerPhase phase)
    {
        Store.Data.Timer.Phase = phase;
        var cut = Render<FocusTimerCard>();
        Assert.That(cut.Markup, Does.Contain("Break in progress"));
        cut.Find(".timer-secondary").Click();
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        cut.Find(".sound-button").Click();
        Assert.That(Store.Data.Muted, Is.True);
        Assert.That(cut.Find(".sound-button").GetAttribute("aria-label"), Is.EqualTo("Turn sounds on"));
    }

    [Test]
    public void TodayPlanCardCompletesSelectsAndUnplansTasks()
    {
        var cut = Render<TodayPlanCard>();
        Assert.That(cut.FindAll(".task-row"), Has.Exactly(1).Items);
        cut.Find(".check-circle").Click();
        Assert.That(PlannedTask.IsComplete, Is.True);
        Assert.That(cut.Find(".task-row").ClassName, Does.Contain("completed"));
        cut.Find(".task-title").Click();
        Assert.That(Store.Data.Timer.TaskId, Is.EqualTo(PlannedTask.Id));
        cut.Find(".row-menu").Click();
        Assert.That(PlannedTask.PlannedToday, Is.False);
        Assert.That(cut.Find(".empty-state").TextContent, Does.Contain("Your day is open"));
        cut.Find(".add-inline").Click();
        Assert.That(Tasks.DialogOpen, Is.True);
        Assert.That(Tasks.Draft.PlannedToday, Is.True);
    }

    [Test]
    public void TodayPlanCardNavigatesToAllTasks()
    {
        var cut = Render<TodayPlanCard>();
        cut.Find(".text-button").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/tasks"));
    }

    [Test]
    public async Task FocusStatsCardUpdatesWhenWorkspaceChanges()
    {
        var cut = Render<FocusStatsCard>();
        Assert.That(Content(cut.Find(".focus-total strong")), Does.Contain("0m"));
        AddSession(30);
        PlannedTask.IsComplete = true;
        await cut.InvokeAsync(Store.SaveAsync);
        cut.WaitForState(() => cut.FindAll(".focus-total strong").Any(element => Content(element).Contains("30m")));
        Assert.That(Content(cut.Find(".focus-total strong")), Does.Contain("30m"));
        Assert.That(cut.FindAll(".mini-stat-row strong").Select(Content), Is.EqualTo(new[] { "1", "1", "1 days 🔥" }));
    }

    [Test]
    public void CoachPromoCardNavigatesToCoach()
    {
        var cut = Render<CoachPromoCard>();
        cut.Find("button").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/coach"));
        Assert.That(cut.Find(".coach-footer").TextContent, Does.Contain("Grounded in your tasks"));
    }

    [Test]
    public void TodayProjectsCardShowsOnlyThreeProjectsAndTheirFocusTime()
    {
        AddSession(20);
        Store.Data.Projects.AddRange(Enumerable.Range(2, 3).Select(i => new ProjectRecord { Name = $"Project {i}" }));
        var cut = Render<TodayProjectsCard>();
        Assert.That(cut.FindAll(".project-meter-row").Count, Is.EqualTo(3));
        Assert.That(Content(cut.Find(".project-meter-row strong")), Does.Contain("20m"));
        cut.Find(".text-button").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/reports"));
    }
}
