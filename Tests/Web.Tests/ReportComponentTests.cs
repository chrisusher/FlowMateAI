using Shared.Enums;
using Web.Components.Features.Reports;

namespace Web.Tests;

public sealed class ReportComponentTests : WorkspaceComponentTest
{
    [Test]
    public void FocusTimeCardDisplaysTheSelectedPeriodTotal()
    {
        Stats.Period = ReportPeriod.Day;
        AddSession(45);
        AddSession(10, DateTimeOffset.UtcNow.AddDays(-10));
        var cut = Render<FocusTimeCard>();
        Assert.That(Content(cut.Find("strong")), Is.EqualTo("45 min"));
        Assert.That(Content(cut.Find("p")), Is.EqualTo("Today"));
    }

    [Test]
    public void SessionCountCardDisplaysCompletedSessions()
    {
        AddSession(25);
        AddSession(15);
        var cut = Render<SessionCountCard>();
        Assert.That(Content(cut.Find("strong")), Is.EqualTo("2"));
        Assert.That(cut.Find("p").TextContent, Does.Contain("Pomodoros completed"));
    }

    [Test]
    public void TopProjectCardShowsTheProjectWithTheMostFocus()
    {
        AddSession(30);
        Store.Data.Projects.Add(new() { Id = "other", Name = "Other" });
        var cut = Render<TopProjectCard>();
        Assert.That(Content(cut.Find("strong")), Is.EqualTo("Personal"));
        Assert.That(cut.Find("p").TextContent, Does.Contain("30 minutes focused"));
    }

    [Test]
    public void TopProjectCardHandlesNoProjectsOrSessions()
    {
        Store.Data.Projects.Clear();
        var cut = Render<TopProjectCard>();
        Assert.That(Content(cut.Find("strong")), Is.EqualTo("No projects yet"));
        Assert.That(cut.Find("p").TextContent, Does.Contain("0 minutes focused"));
    }

    [Test]
    public void TopProjectCardShowsAnEmptyStateWhenProjectsHaveNoFocus()
    {
        var cut = Render<TopProjectCard>();

        Assert.That(Content(cut.Find("strong")), Is.EqualTo("No focus yet"));
        Assert.That(cut.Find("p").TextContent, Does.Contain("0 minutes focused"));
    }

    [Test]
    public async Task FocusChartCardReactsToReportPeriodChanges()
    {
        var cut = Render<FocusChartCard>();
        Assert.That(cut.FindAll(".bar-column").Count, Is.EqualTo(7));
        await cut.InvokeAsync(() => Stats.Period = ReportPeriod.Day);
        cut.WaitForState(() => cut.FindAll(".bar-column").Count == 1);
        Assert.That(cut.FindAll(".bar-column"), Has.Exactly(1).Items);
        Assert.That(Content(cut.Find(".bar-value")), Is.EqualTo("0"));
        Assert.That(cut.Find(".bar-track i").GetAttribute("style"), Does.Contain("4%"));
        await cut.InvokeAsync(() => Stats.Period = ReportPeriod.Month);
        cut.WaitForState(() => cut.FindAll(".bar-column").Count == DateTime.DaysInMonth(Stats.UserNow.Year, Stats.UserNow.Month));
        Assert.That(cut.FindAll(".bar-column").Count, Is.EqualTo(DateTime.DaysInMonth(Stats.UserNow.Year, Stats.UserNow.Month)));
    }

    [Test]
    public void ReportPeriodNavigationUsesMondayWeeksAndStopsAtTheFreeHistoryBoundary()
    {
        Stats.Period = ReportPeriod.Week;
        var cut = Render<ReportsPage>();

        Assert.That(Stats.PeriodStart.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        Assert.That(cut.Find("button[aria-label='Previous period']").HasAttribute("disabled"), Is.False);
        Assert.That(cut.Find("button[aria-label='Next period']").HasAttribute("disabled"), Is.True);

        cut.Find("button[aria-label='Previous period']").Click();
        Assert.That(Stats.PeriodStart, Is.EqualTo(DateOnly.FromDateTime(Stats.UserNow).AddDays(-7 - (((int)Stats.UserNow.DayOfWeek + 6) % 7))));

        Stats.Period = ReportPeriod.Month;
        cut.Find("button[aria-label='Previous period']").Click();
        Assert.That(Stats.CanViewPreviousPeriod, Is.False);
        Assert.That(Stats.EffectivePeriodStart, Is.EqualTo(Stats.AvailableHistoryStart));
        Assert.That(Stats.HistoryIsTruncated, Is.True);
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".history-note"), Has.Exactly(1).Items));
    }

    [Test]
    public void OlderRecordsRemainStoredAndBecomeVisibleAgainWhenProIsRestored()
    {
        Stats.Period = ReportPeriod.Day;
        var oldDate = Stats.TodayDate.AddDays(-45);
        AddSession(23, new DateTimeOffset(oldDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));
        var cut = Render<ReportsPage>();

        Stats.SelectedDate = oldDate;
        cut.WaitForAssertion(() => Assert.That(Stats.HistoryIsTruncated, Is.True));
        Assert.That(Stats.PeriodMinutes, Is.Zero);
        Assert.That(Store.Data.Sessions, Has.Exactly(1).Items);
        Assert.That(cut.Find(".history-note").TextContent, Does.Contain("Pro includes one year of history"));

        Store.Data.Plan = BillingPlan.Pro;
        cut.WaitForAssertion(() => Assert.That(Stats.HistoryIsTruncated, Is.False));
        Assert.That(Stats.PeriodMinutes, Is.EqualTo(23));
        Assert.That(Store.Data.Sessions, Has.Exactly(1).Items);
    }

    [Test]
    public void ReportUsesRecoveredFocusMinutesAcrossTheDaylightSavingChange()
    {
        Store.Data.TimeZone = "Europe/London";
        Store.Data.Plan = BillingPlan.Pro;
        Stats.Period = ReportPeriod.Day;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Store.Data.TimeZone);
        var transitionDate = LastSundayOfOctober(Stats.TodayDate.Year);

        if (transitionDate > Stats.TodayDate)
        {
            transitionDate = LastSundayOfOctober(Stats.TodayDate.Year - 1);
        }

        var localStart = transitionDate.ToDateTime(new TimeOnly(0, 30));
        var localEnd = transitionDate.ToDateTime(new TimeOnly(2, 30));
        localStart = DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified);
        localEnd = DateTime.SpecifyKind(localEnd, DateTimeKind.Unspecified);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeSpan.Zero);
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone), TimeSpan.Zero);
        Stats.SelectedDate = transitionDate;

        Store.RecordFocusIntervals([new() { StartedAt = start, EndedAt = end }], PlannedTask.Id, Project.Id, 180 * 60, "dst-report");

        Assert.That(Store.Data.Sessions.Single().FocusMinutes, Is.EqualTo(180));
        Assert.That(Stats.PeriodMinutes, Is.EqualTo(180));
        Assert.That(Stats.ChartDays.Single().Minutes, Is.EqualTo(180));
    }

    private static DateOnly LastSundayOfOctober(int year)
    {
        var lastOctoberDay = new DateOnly(year, 10, DateTime.DaysInMonth(year, 10));

        return lastOctoberDay.AddDays(-((int)lastOctoberDay.DayOfWeek));
    }

    [Test]
    public void ProjectBreakdownCardDisplaysSharesAndEmptyProjects()
    {
        AddSession(20);
        Store.Data.Projects.Add(new() { Name = "No sessions" });
        var cut = Render<ProjectBreakdownCard>();
        Assert.That(cut.FindAll(".breakdown-row").Count, Is.EqualTo(2));
        Assert.That(Content(cut.Find(".breakdown-row small")), Is.EqualTo("20 min"));
        Assert.That(cut.Find(".breakdown-bar i").GetAttribute("style"), Does.Contain("100%"));
        Assert.That(Content(cut.FindAll(".breakdown-row small")[1]), Is.EqualTo("0 min"));
    }

    [Test]
    public void ProjectBreakdownKeepsArchivedProjectHistoryAndLabel()
    {
        AddSession(20);
        Project.Name = "Legacy client work";
        Project.IsArchived = true;

        var cut = Render<ProjectBreakdownCard>();

        Assert.That(cut.Find(".breakdown-row strong").TextContent, Is.EqualTo("Legacy client work (archived)"));
        Assert.That(Content(cut.Find(".breakdown-row small")), Is.EqualTo("20 min"));
    }

    [Test]
    public void ReportsUseTheLocalDayAssignedByTimerRecoveryAcrossMidnight()
    {
        Store.Data.TimeZone = "Europe/London";
        Stats.Period = ReportPeriod.Day;
        var beforeMidnight = Stats.TodayDate.AddDays(-2).ToDateTime(new TimeOnly(23, 59, 30));
        var localMidnight = beforeMidnight.Date.AddDays(1);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Store.Data.TimeZone);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(beforeMidnight, zone), TimeSpan.Zero);
        var afterMidnight = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight.AddSeconds(30), zone), TimeSpan.Zero);

        Store.RecordFocusIntervals([new() { StartedAt = start, EndedAt = afterMidnight }], PlannedTask.Id, Project.Id, 60, "midnight-report");
        Stats.SelectedDate = DateOnly.FromDateTime(localMidnight);

        Assert.That(Stats.PeriodMinutes, Is.EqualTo(1));
        Assert.That(Stats.ChartDays.Single().Minutes, Is.EqualTo(1));
        Assert.That(Store.Data.Sessions.Single().StartedAt, Is.EqualTo(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, zone), TimeSpan.Zero)));
    }

    [Test]
    public void ReportTotalsKeepRoundedMinutesAcrossPausedFocusIntervals()
    {
        Store.Data.TimeZone = "Europe/London";
        Stats.Period = ReportPeriod.Day;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Store.Data.TimeZone);
        var localStart = Stats.TodayDate.AddDays(-1).ToDateTime(new TimeOnly(10, 0));
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeSpan.Zero);
        FocusIntervalRecord[] intervals =
        [
            new() { StartedAt = start, EndedAt = start.AddMinutes(3) },
            new() { StartedAt = start.AddMinutes(5), EndedAt = start.AddMinutes(7) }
        ];

        Store.RecordFocusIntervals(intervals, PlannedTask.Id, Project.Id, 300, "paused-report");
        Stats.SelectedDate = Stats.TodayDate.AddDays(-1);

        Assert.That(Stats.PeriodMinutes, Is.EqualTo(5));
        Assert.That(Stats.PeriodSessions, Is.EqualTo(1));
        Assert.That(Stats.ChartDays.Single().Minutes, Is.EqualTo(5));
    }

    [Test]
    public async Task FocusTimeCardRecalculatesAfterTimezoneChanges()
    {
        Stats.Period = ReportPeriod.Day;
        Store.Data.TimeZone = "Asia/Tokyo";
        var date = Stats.UserNow.Date;
        var boundary = new DateTimeOffset(date, TimeSpan.FromHours(9));
        AddSession(7, boundary.AddTicks(-1));
        AddSession(11, boundary);
        var cut = Render<FocusTimeCard>();
        Assert.That(Content(cut.Find("strong")), Is.EqualTo("11 min"));
        Store.Data.TimeZone = "America/Los_Angeles";
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).Date;
        var expected = Store.Data.Sessions
            .Where(session => TimeZoneInfo.ConvertTime(session.StartedAt, zone).Date == today)
            .Sum(session => session.FocusMinutes);
        await cut.InvokeAsync(Store.SaveAsync);
        cut.WaitForState(() => cut.FindAll("strong").Any(element => Content(element) == $"{expected} min"));
        Assert.That(Content(cut.Find("strong")), Is.EqualTo($"{expected} min"));
    }
}
