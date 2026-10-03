using System.Text.Json;
using Services.Repositories;
using Shared.Enums;
using Shared.Models;

namespace Services.Reporting;

public sealed record ReportEntry(string SessionId, string? ProjectId, string ProjectName, string? TaskId, string TaskName, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int FocusMinutes, DateOnly LocalDate);
public sealed record ReportDay(DateOnly Date, int TotalMinutes);
public sealed record ReportProject(string ProjectId, string Name, int TotalMinutes, int SessionCount);
public sealed record WorkspaceReport(string TimeZone, string Revision, bool HistoryTruncated, DateOnly EffectiveStart, DateOnly EffectiveEnd, DateOnly AvailableHistoryStart, DateOnly AvailableHistoryEnd, DateOnly QueryDate, DateOnly PeriodStart, DateOnly PeriodEnd, int TotalMinutes, IReadOnlyList<ReportDay> DailyTotals, IReadOnlyList<ReportProject> ProjectTotals, IReadOnlyList<ReportEntry> Entries);
public sealed record ReportPage(WorkspaceReport Summary, IReadOnlyList<ReportEntry> Entries, string? NextCursor);

public interface IWorkspaceReportService
{
    Task<WorkspaceReport> GetAsync(string userId, ReportPeriod period, DateOnly? date, CancellationToken cancellationToken = default);
    Task<ReportPage> GetPageAsync(string userId, ReportPeriod period, DateOnly? date, string? cursor, CancellationToken cancellationToken = default);
    Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceReportService(IWorkspaceReportRepository repository) : IWorkspaceReportService
{
    public Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default) =>
        repository.GetRevisionAsync(userId, cancellationToken);

    public async Task<WorkspaceReport> GetAsync(string userId, ReportPeriod period, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var source = await repository.GetWorkspaceAsync(userId, cancellationToken);
        var workspace = source.Workspace;
        var zone = ResolveZone(workspace.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var selected = date ?? today;
        var (start, end) = Period(period, selected);
        var billing = await repository.GetBillingAccessAsync(userId, cancellationToken);
        var entitled = billing is { Plan: BillingPlan.Pro, Entitlement: SubscriptionEntitlement.Entitled };
        var historyStart = entitled ? today.AddYears(-1).AddDays(1) : today.AddDays(-29);
        var effectiveStart = start < historyStart ? historyStart : start;
        var effectiveEnd = end;
        var truncated = effectiveStart > start;

        if (effectiveStart > today || effectiveStart > effectiveEnd)
        {
            throw new ReportHistoryUnavailableException();
        }

        var projects = workspace.Projects.ToDictionary(x => x.Id, x => x.Name, StringComparer.Ordinal);
        var tasks = workspace.Tasks.ToDictionary(x => x.Id, x => x.Title, StringComparer.Ordinal);
        
        var entries = workspace.Sessions.Where(s => s.FocusMinutes > 0 && s.EndedAt > s.StartedAt)
            .Select(s => (Session: s, Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.StartedAt, zone).DateTime)))
            .Where(x => x.Day >= effectiveStart && x.Day <= effectiveEnd)
            .OrderBy(x => x.Session.StartedAt)
            .Select(x => new ReportEntry(x.Session.Id, x.Session.ProjectId, projects.GetValueOrDefault(x.Session.ProjectId, "Deleted project"), x.Session.TaskId,
                x.Session.TaskId is { } taskId ? tasks.GetValueOrDefault(taskId, "Deleted task") : "", x.Session.StartedAt, x.Session.EndedAt, x.Session.FocusMinutes, x.Day)).ToArray();
        var days = Enumerable.Range(0, effectiveEnd.DayNumber - effectiveStart.DayNumber + 1)
            .Select(i => new DateOnly(effectiveStart.Year, effectiveStart.Month, effectiveStart.Day).AddDays(i))
            .Select(day => new ReportDay(day, entries.Where(e => e.LocalDate == day).Sum(e => e.FocusMinutes))).ToArray();
        var totalsByProject = entries.GroupBy(e => e.ProjectId ?? "").ToDictionary(g => g.Key, g => (Minutes: g.Sum(e => e.FocusMinutes), Count: g.Count()), StringComparer.Ordinal);
        var projectTotals = workspace.Projects.Select(p => new ReportProject(p.Id, p.Name, totalsByProject.GetValueOrDefault(p.Id).Minutes, totalsByProject.GetValueOrDefault(p.Id).Count))
            .Concat(entries.Where(e => e.ProjectId is not null && !projects.ContainsKey(e.ProjectId)).GroupBy(e => e.ProjectId!).Select(g => new ReportProject(g.Key, "Deleted project", g.Sum(e => e.FocusMinutes), g.Count())))
            .ToArray();

        return new(workspace.TimeZone, source.Revision, truncated, effectiveStart, effectiveEnd, historyStart, today, selected, start, end,
            entries.Sum(e => e.FocusMinutes), days, projectTotals, entries);
    }

    public async Task<ReportPage> GetPageAsync(string userId, ReportPeriod period, DateOnly? date, string? cursor, CancellationToken cancellationToken = default)
    {
        var report = await GetAsync(userId, period, date, cancellationToken);
        var offset = 0;

        if (!string.IsNullOrEmpty(cursor))
        {
            ReportPageCursor? decoded;

            try
            {
                decoded = JsonSerializer.Deserialize<ReportPageCursor>(Convert.FromBase64String(cursor));
            }
            catch (FormatException)
            {
                throw new InvalidReportCursorException();
            }
            catch (JsonException)
            {
                throw new InvalidReportCursorException();
            }

            if (decoded is null || decoded.UserId != userId || decoded.Period != period.ToWireValue() || decoded.QueryDate != report.QueryDate.ToString("yyyy-MM-dd") || decoded.Offset < 0)
            {
                throw new InvalidReportCursorException();
            }

            if (decoded.Revision != report.Revision)
            {
                throw new ReportRevisionChangedException();
            }

            offset = decoded.Offset;
        }
        var page = report.Entries.Skip(offset).Take(100).ToArray();
        var next = offset + page.Length < report.Entries.Count
            ? ReportPageCursor.Encode(userId, period, report.QueryDate, offset + page.Length, report.Revision)
            : null;

        return new(report, page, next);
    }

    private static (DateOnly Start, DateOnly End) Period(ReportPeriod period, DateOnly date) => period switch
    {
        ReportPeriod.Day => (date, date),
        ReportPeriod.Week => (date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), date.AddDays(6 - (((int)date.DayOfWeek + 6) % 7))),
        ReportPeriod.Month => (new(date.Year, date.Month, 1), new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null)
    };

    private static TimeZoneInfo ResolveZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidWorkspaceTimeZoneException();
        }
    }

}

public sealed class ReportHistoryUnavailableException : Exception;
public sealed class ReportRevisionChangedException : Exception;
public sealed class InvalidReportCursorException : Exception;
public sealed class InvalidWorkspaceTimeZoneException : Exception;

public sealed record ReportPageCursor(string UserId, string Period, string QueryDate, int Offset, string Revision)
{
    public static string Encode(string userId, ReportPeriod period, DateOnly queryDate, int offset, string revision)
    {
        var cursor = new ReportPageCursor(userId, period.ToWireValue(), queryDate.ToString("yyyy-MM-dd"), offset, revision);
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor));
    }
}
