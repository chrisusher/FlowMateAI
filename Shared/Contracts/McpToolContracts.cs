namespace Shared.Contracts;

public interface IMcpToolResponse
{
}

public sealed record McpToolErrorResponse(bool IsError, string Code, string Message) : IMcpToolResponse;

public sealed record McpRateLimitErrorResponse(
    bool IsError,
    string Code,
    int PerMinuteLimit,
    int PerDayLimit,
    int MinuteRemaining,
    int DayRemaining,
    int RetryAfterSeconds,
    DateTimeOffset MinuteResetAt,
    DateTimeOffset DayResetAt) : IMcpToolResponse;

public sealed record McpProjectSummary(string ProjectId, string Name);

public sealed record McpRateLimitPolicy(int PerMinute, int PerUtcDay);

public sealed record McpListProjectsResponse(
    IReadOnlyList<McpProjectSummary> Projects,
    string TimeZone,
    DateOnly HistoryAvailableFrom,
    DateOnly HistoryAvailableThrough,
    McpRateLimitPolicy? FreeRateLimit,
    bool ProExempt) : IMcpToolResponse;

public sealed record McpDailyTotal(DateOnly Date, int TotalMinutes);

public sealed record McpProjectTotal(string ProjectId, string Name, int TotalMinutes, int SessionCount);

public sealed record McpTimesheetEntry(
    string SessionId,
    string? ProjectId,
    string ProjectName,
    string? TaskId,
    string TaskName,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int FocusMinutes,
    DateOnly LocalDate);

public sealed record McpTimesheetResponse(
    string TimeZone,
    bool HistoryTruncated,
    DateOnly EffectiveStart,
    DateOnly EffectiveEnd,
    int TotalMinutes,
    IReadOnlyList<McpDailyTotal> DailyTotals,
    IReadOnlyList<McpProjectTotal> ProjectTotals,
    IReadOnlyList<McpTimesheetEntry> Entries,
    string? NextCursor) : IMcpToolResponse;

public sealed record McpProjectTimeResponse(
    string ProjectId,
    string ProjectName,
    int TotalMinutes,
    int SessionCount,
    IReadOnlyList<McpDailyTotal> DailyBreakdown) : IMcpToolResponse;
