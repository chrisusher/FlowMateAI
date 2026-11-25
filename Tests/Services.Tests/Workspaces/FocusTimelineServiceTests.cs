using Services.Reporting;
using Services.Repositories;
using Shared.Enums;

namespace Services.Tests.Workspaces;

public sealed class FocusTimelineServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task GetAsync_PausedAndLegacySessions_ReturnsExactIntervalsAndApproximateSpan()
    {
        var start = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
        var workspace = Workspace();
        workspace.Sessions.Add(new()
        {
            Id = "exact",
            ProjectId = "deleted",
            ProjectName = "Former project",
            ProjectColour = "#123456",
            TaskId = "deleted-task",
            TaskName = "Former task",
            StartedAt = start,
            EndedAt = start.AddMinutes(20),
            FocusSeconds = 600,
            Intervals =
            [
                new()
                {
                    StartedAt = start,
                    EndedAt = start.AddMinutes(5)
                },
                new()
                {
                    StartedAt = start.AddMinutes(15),
                    EndedAt = start.AddMinutes(20)
                }
            ]
        });
        workspace.Sessions.Add(new()
        {
            Id = "legacy",
            ProjectId = "deleted",
            StartedAt = start.AddHours(1),
            EndedAt = start.AddHours(2),
            FocusSeconds = 1800
        });
        var service = Service(workspace);

        var result = await service.GetAsync("user", null);

        Assert.That(result.WeekStart, Is.EqualTo(new DateOnly(2026, 11, 23)));
        Assert.That(result.Entries, Has.Count.EqualTo(3));
        Assert.That(result.Entries.Take(2).All(entry => !entry.IsApproximate), Is.True);
        Assert.That(result.Entries.Take(2).Select(entry => entry.FocusSeconds), Is.EqualTo(new[] { 300d, 300d }));
        Assert.That(result.Entries[0].ProjectName, Is.EqualTo("Former project"));
        Assert.That(result.Entries[0].TaskName, Is.EqualTo("Former task"));
        Assert.That(result.Entries[2].IsApproximate, Is.True);
        Assert.That(result.Entries[2].FocusSeconds, Is.EqualTo(1800));
    }

    [Test]
    public async Task GetAsync_MidnightAndAutumnClockChange_SplitsWithoutLosingElapsedTime()
    {
        var workspace = Workspace();
        var start = new DateTimeOffset(2026, 10, 24, 22, 30, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.Zero);
        workspace.Sessions.Add(new()
        {
            ProjectId = "p",
            StartedAt = start,
            EndedAt = end,
            FocusSeconds = (end - start).TotalSeconds,
            Intervals = [new()
            {
                StartedAt = start,
                EndedAt = end
            }]
        });
        var result = await Service(workspace, pro: true).GetAsync("user", new DateOnly(2026, 10, 25));

        Assert.That(result.Entries, Has.Count.EqualTo(3));
        Assert.That(result.Entries.Select(entry => entry.LocalDate), Is.EqualTo(new[] { new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 25), new DateOnly(2026, 10, 25) }));
        Assert.That(result.Entries.Sum(entry => entry.FocusSeconds), Is.EqualTo((end - start).TotalSeconds));
        Assert.That(result.Entries[1].EndedAt, Is.EqualTo(new DateTimeOffset(2026, 10, 25, 1, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public async Task GetAsync_SpringClockChange_SplitsAtOffsetTransition()
    {
        var workspace = Workspace();
        var start = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero);
        var end = start.AddHours(2);
        workspace.Sessions.Add(new()
        {
            ProjectId = "p",
            StartedAt = start,
            EndedAt = end,
            FocusSeconds = 7200,
            Intervals = [new()
            {
                StartedAt = start,
                EndedAt = end
            }]
        });

        var result = await Service(workspace, pro: true).GetAsync("user", new DateOnly(2026, 3, 29));

        Assert.That(result.Entries, Has.Count.EqualTo(2));
        Assert.That(result.Entries[0].EndedAt, Is.EqualTo(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero)));
        Assert.That(result.Entries.Sum(entry => entry.FocusSeconds), Is.EqualTo(7200));
    }

    [Test]
    public async Task GetAsync_WeekAtFreeHistoryBoundary_ReportsPartialAvailability()
    {
        var result = await Service(Workspace()).GetAsync("user", new DateOnly(2026, 10, 27));

        Assert.That(result.HistoryAvailableFrom, Is.EqualTo(new DateOnly(2026, 10, 27)));
        Assert.That(result.WeekStart, Is.EqualTo(new DateOnly(2026, 10, 26)));
        Assert.That(result.HistoryTruncated, Is.True);
    }

    [TestCase(2026, 10, 19)]
    [TestCase(2026, 11, 30)]
    public void GetAsync_OutsideAvailableWeeks_RejectsRequest(int year, int month, int day)
    {
        Assert.That(async () => await Service(Workspace()).GetAsync("user", new DateOnly(year, month, day)), Throws.TypeOf<ReportHistoryUnavailableException>());
    }

    private static WorkspaceSnapshot Workspace() => new()
    {
        TimeZone = "Europe/London"
    };
    private static FocusTimelineService Service(WorkspaceSnapshot workspace, bool pro = false) => new(new Repository(workspace, pro), new FixedTimeProvider());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Repository(WorkspaceSnapshot workspace, bool pro) : IWorkspaceReportRepository
    {
        public Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult("revision");
        public Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(new WorkspaceReportSource(workspace, "revision"));
        public Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<BillingAccess?>(pro ? new(BillingPlan.Pro, SubscriptionEntitlement.Entitled) : null);
    }
}
