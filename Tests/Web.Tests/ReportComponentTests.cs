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
