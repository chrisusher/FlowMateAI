using Shared.Contracts;

namespace Services.Reporting;

public static class TaskFocusAggregation
{
    public static IReadOnlyList<TaskFocus> FromEntries(IEnumerable<ReportEntry> entries) =>
        entries.Where(entry => !string.IsNullOrWhiteSpace(entry.TaskId))
            .GroupBy(entry => entry.TaskId!, StringComparer.Ordinal)
            .Select(group => new TaskFocus(group.Key, group.LastOrDefault(entry => !string.IsNullOrWhiteSpace(entry.TaskName) && entry.TaskName != "Deleted task")?.TaskName ?? "Deleted task", ReportEntry.TotalMinutes(group), Math.Round(ReportEntry.TotalSessions(group), 1, MidpointRounding.AwayFromZero)))
            .OrderByDescending(task => task.TotalMinutes).ThenBy(task => task.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}
