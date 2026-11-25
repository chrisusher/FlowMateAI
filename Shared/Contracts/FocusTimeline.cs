namespace Shared.Contracts;

public sealed record FocusTimelineEntry(
    string SessionId,
    string ProjectName,
    string ProjectColour,
    string TaskName,
    DateOnly LocalDate,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    double FocusSeconds,
    bool IsApproximate);

public sealed record FocusTimeline(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    string TimeZone,
    DateOnly HistoryAvailableFrom,
    DateOnly HistoryAvailableThrough,
    bool HistoryTruncated,
    IReadOnlyList<FocusTimelineEntry> Entries);
