using Services.Repositories;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace Services.Reporting;

public interface IFocusTimelineService
{
    Task<FocusTimeline> GetAsync(string userId, DateOnly? date, CancellationToken cancellationToken = default);
}

public sealed class FocusTimelineService(IWorkspaceReportRepository repository, TimeProvider? clock = null) : IFocusTimelineService
{
    public async Task<FocusTimeline> GetAsync(string userId, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var source = await repository.GetWorkspaceAsync(userId, cancellationToken);
        var workspace = source.Workspace;
        TimeZoneInfo zone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(workspace.TimeZone);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidWorkspaceTimeZoneException();
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime((clock ?? TimeProvider.System).GetUtcNow(), zone).DateTime);
        var selected = date ?? today;
        var start = selected.AddDays(-(((int)selected.DayOfWeek + 6) % 7));
        var end = start.AddDays(6);
        var billing = await repository.GetBillingAccessAsync(userId, cancellationToken);
        var historyStart = billing is { Plan: BillingPlan.Pro, Entitlement: SubscriptionEntitlement.Entitled }
            ? today.AddYears(-1).AddDays(1)
            : today.AddDays(-29);

        if (start > today || end < historyStart)
        {
            throw new ReportHistoryUnavailableException();
        }

        var projects = workspace.Projects.ToDictionary(project => project.Id, StringComparer.Ordinal);
        var tasks = workspace.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        var entries = new List<FocusTimelineEntry>();
        var availableStart = start > historyStart ? start : historyStart;
        var availableEnd = end < today ? end : today;
        var windowStart = LocalMidnight(availableStart, zone);
        var windowEnd = LocalMidnight(availableEnd.AddDays(1), zone);

        foreach (var session in workspace.Sessions.Where(session => session.ElapsedSeconds > 0 && session.EndedAt > session.StartedAt))
        {
            projects.TryGetValue(session.ProjectId, out var project);
            TaskRecord? task = null;

            if (session.TaskId is not null)
            {
                tasks.TryGetValue(session.TaskId, out task);
            }

            var approximate = session.Intervals is not { Count: > 0 };
            var intervals = approximate
                ? [new FocusIntervalRecord
                {
                    StartedAt = session.StartedAt,
                    EndedAt = session.EndedAt
                }]
                : session.Intervals;

            foreach (var interval in intervals)
            {
                var cursor = interval.StartedAt > windowStart ? interval.StartedAt : windowStart;
                var intervalEnd = interval.EndedAt < windowEnd ? interval.EndedAt : windowEnd;

                while (cursor < intervalEnd)
                {
                    var local = TimeZoneInfo.ConvertTime(cursor, zone);
                    var day = DateOnly.FromDateTime(local.DateTime);
                    var boundary = LocalMidnight(day.AddDays(1), zone);
                    var segmentEnd = boundary > cursor && boundary < intervalEnd ? boundary : intervalEnd;
                    segmentEnd = FirstOffsetTransition(zone, cursor, segmentEnd) ?? segmentEnd;

                    if (day >= start && day <= end && day >= historyStart && day <= today)
                    {
                        var duration = (segmentEnd - cursor).TotalSeconds;

                        if (approximate)
                        {
                            duration *= session.ElapsedSeconds / (session.EndedAt - session.StartedAt).TotalSeconds;
                        }
                        entries.Add(new FocusTimelineEntry(session.Id,
                            project?.Name ?? session.ProjectName ?? "Deleted project",
                            project?.Color ?? session.ProjectColour ?? "#777777",
                            task?.Title ?? session.TaskName ?? (session.TaskId is null ? "No task" : "Deleted task"),
                            day, cursor, segmentEnd, duration, approximate));
                    }
                    cursor = segmentEnd;
                }
            }
        }

        return new FocusTimeline(start, end, workspace.TimeZone, historyStart, today, start < historyStart,
            entries.OrderBy(entry => entry.StartedAt).ToArray());
    }
    private static DateTimeOffset LocalMidnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static DateTimeOffset? FirstOffsetTransition(TimeZoneInfo zone, DateTimeOffset start, DateTimeOffset end)
    {
        var offset = zone.GetUtcOffset(start);
        var previous = start;

        for (var probe = start.AddHours(1); previous < end; probe = probe.AddHours(1))
        {
            var current = probe < end ? probe : end;

            if (zone.GetUtcOffset(current) != offset)
            {
                var low = previous.UtcTicks;
                var high = current.UtcTicks;

                while (high - low > 1)
                {
                    var middle = low + (high - low) / 2;

                    if (zone.GetUtcOffset(new DateTimeOffset(middle, TimeSpan.Zero)) == offset)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                return new DateTimeOffset(high, TimeSpan.Zero);
            }

            if (current == end)
            {
                break;
            }
            previous = current;
        }

        return null;
    }

}
