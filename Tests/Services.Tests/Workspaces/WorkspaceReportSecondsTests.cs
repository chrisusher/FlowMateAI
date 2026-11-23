using Services.Repositories;
using Services.Reporting;

namespace Services.Tests.Workspaces;

public sealed class WorkspaceReportSecondsTests
{
    [Test]
    public async Task ReportAggregatesSecondsAcrossTasksAndIncludesLegacyMinutes()
    {
        var start = DateTimeOffset.UtcNow.Date.AddHours(10);
        var instant = new DateTimeOffset(start, TimeSpan.Zero);
        var workspace = new WorkspaceSnapshot
        {
            TimeZone = "UTC",
            Projects = [new() { Id = "project", Name = "Project" }],
            Sessions =
            [
                new() { Id = "one", ProjectId = "project", TaskId = "one", StartedAt = instant, EndedAt = instant.AddSeconds(30), FocusSeconds = 30 },
                new() { Id = "two", ProjectId = "project", TaskId = "two", StartedAt = instant.AddSeconds(30), EndedAt = instant.AddMinutes(1), FocusSeconds = 30 },
                new() { Id = "legacy", ProjectId = "project", StartedAt = instant.AddMinutes(1), EndedAt = instant.AddMinutes(3), FocusMinutes = 2 }
            ]
        };
        var report = await new WorkspaceReportService(new ReportRepository(workspace)).GetAsync("user", ReportPeriod.Day, null);

        Assert.That(report.TotalMinutes, Is.EqualTo(3));
        Assert.That(report.DailyTotals.Single().TotalMinutes, Is.EqualTo(3));
        Assert.That(report.ProjectTotals.Single().TotalMinutes, Is.EqualTo(3));
        Assert.That(report.Entries, Has.Count.EqualTo(3));
        Assert.That(report.Entries.Take(2).Select(entry => entry.FocusSeconds), Is.EqualTo(new double?[] { 30, 30 }));
    }

    private sealed class ReportRepository(WorkspaceSnapshot workspace) : IWorkspaceReportRepository
    {
        public Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult("revision");
        public Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceReportSource(workspace, "revision"));
        public Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<BillingAccess?>(null);
    }
}
