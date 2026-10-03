using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services.Mcp;
using Services.Reporting;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace MCP;

public interface IMcpToolInvocation
{
    Task<IMcpToolResponse> InvokeAsync(ToolInvocationContext context, string? period = null, string? date = null, string? cursor = null, string? projectId = null, bool projectsOnly = false, CancellationToken cancellationToken = default);
}

public sealed class McpToolInvocation(IMcpCredentialService credentials, IWorkspaceReportService reports, IFreeUsageLimiter limiter, CosmosClient cosmos, IConfiguration configuration, ILogger<McpToolInvocation> logger) : IMcpToolInvocation
{
    private static readonly Meter Meter = new("FlowMateAI.MCP");
    private static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>("flowmate.mcp.tool.duration", "ms");

    public async Task<IMcpToolResponse> InvokeAsync(ToolInvocationContext context, string? period = null, string? date = null, string? cursor = null, string? projectId = null, bool projectsOnly = false, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();

        try
        {
            return await InvokeCoreAsync(context, period, date, cursor, projectId, projectsOnly, cancellationToken);
        }
        finally
        {
            ToolDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("tool", context.Name));
        }
    }

    private async Task<IMcpToolResponse> InvokeCoreAsync(ToolInvocationContext context, string? period, string? date, string? cursor, string? projectId, bool projectsOnly, CancellationToken cancellationToken)
    {
        if (!context.TryGetHttpTransport(out var transport) || transport is null || !transport.Headers.TryGetValue("X-Api-Key", out var key))
        {
            logger.LogInformation("MCP authentication rejected: API key header missing.");

            return Error("unauthorized", "Provide a valid X-Api-Key header.");
        }
        ValidatedMcpKey? validated;
        bool isPro;

        try
        {
            validated = await credentials.ValidateAsync(key, cancellationToken);

            if (validated is null)
            {
                logger.LogInformation("MCP authentication rejected: API key invalid or inactive.");

                return Error("unauthorized", "The API key is invalid, expired, or revoked.");
            }
            isPro = await IsProAsync(validated.UserId, cancellationToken);
        }
        catch (CosmosException)
        {
            logger.LogWarning("MCP credential or entitlement dependency failed.");

            return Error("temporarily_unavailable", "Account credentials are temporarily unavailable.");
        }
        var periodValue = period ?? ReportPeriod.Day.ToWireValue();

        if (!ReportPeriodExtensions.TryParseWireValue(periodValue, out var reportPeriod))
        {
            return Error("invalid_arguments", "period must be day, week, or month.");
        }

        DateOnly? selectedDate = null;

        if (date is not null && (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed) || (selectedDate = parsed) is null))
        {
            return Error("invalid_arguments", "date must be an ISO calendar date (YYYY-MM-DD).");
        }

        if (projectId is not null && string.IsNullOrWhiteSpace(projectId))
        {
            return Error("invalid_arguments", "projectId must not be empty.");
        }

        if (cursor is { Length: > 2048 })
        {
            return Error("invalid_arguments", "cursor is invalid.");
        }

        if (!isPro)
        {
            try
            {
                var admission = await limiter.AdmitAsync(validated.UserId, cancellationToken);

                if (!admission.Admitted)
                {
                    logger.LogInformation("MCP Free account throttled by a configured rate limit.");

                    return new McpRateLimitErrorResponse(true, "rate_limited", admission.PerMinuteLimit, admission.PerDayLimit, admission.MinuteRemaining, admission.DayRemaining, admission.RetryAfterSeconds, admission.MinuteResetAt, admission.DayResetAt);
                }
            }
            catch (UsageAdmissionUnavailableException) { return Error("temporarily_unavailable", "Rate limit admission is temporarily unavailable. Retry shortly."); }
            catch (CosmosException)
            {
                logger.LogWarning("MCP rate-limit storage dependency failed.");

                return Error("temporarily_unavailable", "Rate limit admission is temporarily unavailable. Retry shortly.");
            }
        }

        try
        {
            var report = await reports.GetAsync(validated.UserId, reportPeriod, selectedDate, cancellationToken);

            if (projectsOnly)
            {
                return new McpListProjectsResponse(
                    report.ProjectTotals.Select(project => new McpProjectSummary(project.ProjectId, project.Name)).ToArray(),
                    report.TimeZone,
                    report.AvailableHistoryStart,
                    report.AvailableHistoryEnd,
                    isPro ? null : new McpRateLimitPolicy(limiter.PerMinuteLimit, limiter.PerDayLimit),
                    isPro);
            }

            if (projectId is not null && !report.ProjectTotals.Any(p => p.ProjectId == projectId))
            {
                return Error("project_not_found", "The project is not in this account.");
            }

            if (projectId is not null)
            {
                var chosen = report.ProjectTotals.First(p => p.ProjectId == projectId);

                return new McpProjectTimeResponse(
                    chosen.ProjectId,
                    chosen.Name,
                    chosen.TotalMinutes,
                    chosen.SessionCount,
                    report.DailyTotals.Select(day => new McpDailyTotal(
                        day.Date,
                        report.Entries.Where(entry => entry.ProjectId == projectId && entry.LocalDate == day.Date).Sum(entry => entry.FocusMinutes))).ToArray());
            }

            if (cursor is not null)
            {
                var page = await reports.GetPageAsync(validated.UserId, reportPeriod, selectedDate, cursor, cancellationToken);

                return ToTimesheetResponse(page.Summary, page.Entries, page.NextCursor);
            }

            var nextCursor = report.Entries.Count > 100 ? CreateFirstCursor(validated.UserId, reportPeriod, report) : null;

            return ToTimesheetResponse(report, report.Entries.Take(100), nextCursor);
        }
        catch (ReportHistoryUnavailableException)
        {
            return Error("history_unavailable", "This period is outside the available report history.");
        }
        catch (ReportRevisionChangedException)
        {
            return Error("revision_changed", "Workspace data changed. Restart pagination without a cursor.");
        }
        catch (InvalidReportCursorException)
        {
            return Error("invalid_cursor", "The pagination cursor is invalid for this query.");
        }
        catch (InvalidWorkspaceTimeZoneException)
        {
            return Error("invalid_time_zone", "The workspace has an invalid time zone.");
        }
        catch (CosmosException)
        {
            logger.LogWarning("MCP report data dependency failed for an authenticated account."); return Error("temporarily_unavailable", "Report data is temporarily unavailable.");
        }
        catch (Exception)
        {
            logger.LogWarning("MCP report invocation failed."); return Error("temporarily_unavailable", "Report data is temporarily unavailable.");
        }
    }

    private async Task<bool> IsProAsync(string userId, CancellationToken token)
    {
        var database = configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development";

        try
        {
            var item = await cosmos.GetContainer(database, "BillingEntitlements").ReadItemAsync<Entitlement>("billing", new PartitionKey(userId), cancellationToken: token);

            return BillingPlanExtensions.ParseOrFree(item.Resource.Plan) == BillingPlan.Pro &&
                SubscriptionEntitlementExtensions.FromProviderStatus(item.Resource.SubscriptionStatus) == SubscriptionEntitlement.Entitled;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
    private static McpToolErrorResponse Error(string code, string message) => new(true, code, message);

    private static McpTimesheetResponse ToTimesheetResponse(WorkspaceReport report, IEnumerable<ReportEntry> entries, string? nextCursor) => new(
        report.TimeZone,
        report.HistoryTruncated,
        report.EffectiveStart,
        report.EffectiveEnd,
        report.TotalMinutes,
        report.DailyTotals.Select(day => new McpDailyTotal(day.Date, day.TotalMinutes)).ToArray(),
        report.ProjectTotals.Select(project => new McpProjectTotal(project.ProjectId, project.Name, project.TotalMinutes, project.SessionCount)).ToArray(),
        entries.Select(entry => new McpTimesheetEntry(entry.SessionId, entry.ProjectId, entry.ProjectName, entry.TaskId, entry.TaskName, entry.StartedAt, entry.EndedAt, entry.FocusMinutes, entry.LocalDate)).ToArray(),
        nextCursor);

    private static string CreateFirstCursor(string userId, ReportPeriod period, WorkspaceReport report) =>
        ReportPageCursor.Encode(userId, period, report.QueryDate, 100, report.Revision);

    private sealed class Entitlement { public string Plan { get; set; } = BillingPlan.Free.ToString(); 
    
    public string SubscriptionStatus { get; set; } = "none"; }
}
