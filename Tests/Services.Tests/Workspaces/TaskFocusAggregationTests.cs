using Services.Reporting;

namespace Services.Tests.Workspaces;

public sealed class TaskFocusAggregationTests
{
    [Test]
    public void FromEntries_MixedSessions_GroupsTasksAndPreservesLegacyCounts()
    {
        var now = DateTimeOffset.UtcNow;
        var day = DateOnly.FromDateTime(now.DateTime);
        ReportEntry Entry(string id, string? task, string name, int minutes, double? seconds, int? planned) =>
            new(id, "project", "Project", task, name, now, now.AddMinutes(1), minutes, day)
            {
                FocusSeconds = seconds,
                PlannedSeconds = planned
            };
        var entries = new[]
        {
            Entry("a", "one", "Current task", 0, 90, 1500),
            Entry("b", "one", "Current task", 0, 90, 1500),
            Entry("c", "one", "Current task", 2, null, null),
            Entry("d", "deleted", "Recorded name", 1, null, null),
            Entry("e", null, "", 50, null, null)
        };

        var result = TaskFocusAggregation.FromEntries(entries);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].TotalMinutes, Is.EqualTo(5));
        Assert.That(result[0].Pomodoros, Is.EqualTo(1.1));
        Assert.That(result[1].Name, Is.EqualTo("Recorded name"));
    }
}
