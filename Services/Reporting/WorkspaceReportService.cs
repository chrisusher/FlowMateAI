using System.Text;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Shared.Models;

namespace Services.Reporting;

public sealed record ReportEntry(string SessionId, string? ProjectId, string ProjectName, string? TaskId, string TaskName, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int FocusMinutes, DateOnly LocalDate);
public sealed record ReportDay(DateOnly Date, int TotalMinutes);
public sealed record ReportProject(string ProjectId, string Name, int TotalMinutes, int SessionCount);
public sealed record WorkspaceReport(string TimeZone, string Revision, bool HistoryTruncated, DateOnly EffectiveStart, DateOnly EffectiveEnd, DateOnly AvailableHistoryStart, DateOnly AvailableHistoryEnd, DateOnly QueryDate, DateOnly PeriodStart, DateOnly PeriodEnd, int TotalMinutes, IReadOnlyList<ReportDay> DailyTotals, IReadOnlyList<ReportProject> ProjectTotals, IReadOnlyList<ReportEntry> Entries);
public sealed record ReportPage(WorkspaceReport Summary, IReadOnlyList<ReportEntry> Entries, string? NextCursor);

public interface IWorkspaceReportService
{
    Task<WorkspaceReport> GetAsync(string userId, string period, DateOnly? date, CancellationToken cancellationToken = default);
    Task<ReportPage> GetPageAsync(string userId, string period, DateOnly? date, string? cursor, CancellationToken cancellationToken = default);
    Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceReportService(CosmosClient cosmos, IConfiguration configuration) : IWorkspaceReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Container WorkspaceContainer => cosmos.GetContainer(configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development", "WorkspaceDocuments");

    public async Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await WorkspaceContainer.ReadItemAsync<WorkspaceEnvelope>("workspace", new PartitionKey(userId), cancellationToken: cancellationToken);
            return response.ETag ?? response.Resource.UpdatedAt.UtcTicks.ToString();
        }
        catch (CosmosException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { return "missing"; }
    }

    public async Task<WorkspaceReport> GetAsync(string userId, string period, DateOnly? date, CancellationToken cancellationToken = default)
    {
        var document = await WorkspaceContainer.ReadItemAsync<WorkspaceEnvelope>("workspace", new PartitionKey(userId), cancellationToken: cancellationToken);
        var workspace = JsonSerializer.Deserialize<WorkspaceSnapshot>(document.Resource.Payload, JsonOptions) ?? new();
        var zone = ResolveZone(workspace.TimeZone);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var selected = date ?? today;
        var (start, end) = Period(period, selected);
        var entitled = false;
        try
        {
            var bill = await cosmos.GetContainer(configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development", "BillingEntitlements")
                .ReadItemAsync<BillingEnvelope>("billing", new PartitionKey(userId), cancellationToken: cancellationToken);
            entitled = bill.Resource.Plan.Equals("Pro", StringComparison.OrdinalIgnoreCase) && bill.Resource.SubscriptionStatus is "active" or "trialing";
        }
        catch (CosmosException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        var historyStart = entitled ? today.AddYears(-1).AddDays(1) : today.AddDays(-29);
        var effectiveStart = start < historyStart ? historyStart : start;
        var effectiveEnd = end;
        var truncated = effectiveStart > start;
        if (effectiveStart > today || effectiveStart > effectiveEnd) throw new ReportHistoryUnavailableException();

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
        return new(workspace.TimeZone, document.ETag ?? document.Resource.UpdatedAt.UtcTicks.ToString(), truncated, effectiveStart, effectiveEnd, historyStart, today, selected, start, end,
            entries.Sum(e => e.FocusMinutes), days, projectTotals, entries);
    }

    public async Task<ReportPage> GetPageAsync(string userId, string period, DateOnly? date, string? cursor, CancellationToken cancellationToken = default)
    {
        var report = await GetAsync(userId, period, date, cancellationToken);
        var offset = 0;
        if (!string.IsNullOrEmpty(cursor))
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var pieces = decoded.Split('|');
            if (pieces.Length != 5 || pieces[0] != userId || pieces[1] != period || pieces[2] != report.QueryDate.ToString("yyyy-MM-dd")) throw new InvalidReportCursorException();
            if (pieces[4] != report.Revision) throw new ReportRevisionChangedException();
            if (!int.TryParse(pieces[3], out offset) || offset < 0) throw new InvalidReportCursorException();
        }
        var page = report.Entries.Skip(offset).Take(100).ToArray();
        var next = offset + page.Length < report.Entries.Count ? Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}|{period}|{report.QueryDate:yyyy-MM-dd}|{offset + page.Length}|{report.Revision}")) : null;
        return new(report, page, next);
    }

    private static (DateOnly Start, DateOnly End) Period(string period, DateOnly date) => period.ToLowerInvariant() switch
    {
        "day" => (date, date), "week" => (date.AddDays(-(((int)date.DayOfWeek + 6) % 7)), date.AddDays(6 - (((int)date.DayOfWeek + 6) % 7))),
        "month" => (new(date.Year, date.Month, 1), new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))),
        _ => throw new ArgumentException("period must be day, week, or month.")
    };
    private static TimeZoneInfo ResolveZone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new InvalidWorkspaceTimeZoneException(); } }
    private sealed class WorkspaceEnvelope { public string Id { get; set; } = ""; public string UserId { get; set; } = ""; public string Payload { get; set; } = "{}"; public DateTimeOffset UpdatedAt { get; set; } public string? ETag { get; set; } }
    private sealed class BillingEnvelope { public string Plan { get; set; } = "Free"; public string SubscriptionStatus { get; set; } = "none"; }
}

public sealed class ReportHistoryUnavailableException : Exception;
public sealed class ReportRevisionChangedException : Exception;
public sealed class InvalidReportCursorException : Exception;
public sealed class InvalidWorkspaceTimeZoneException : Exception;
