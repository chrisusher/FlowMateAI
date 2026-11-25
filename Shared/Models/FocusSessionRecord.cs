using System.Text.Json.Serialization;

namespace Shared.Models;

public sealed class FocusSessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? TaskId { get; set; }
    public string ProjectId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int FocusMinutes { get; set; }
    public double? FocusSeconds { get; set; }
    public int? PlannedSeconds { get; set; }
    public List<FocusIntervalRecord> Intervals { get; set; } = [];

    [JsonIgnore]
    public double ElapsedSeconds => FocusSeconds ?? FocusMinutes * 60d;

    public static double TotalSessions(IEnumerable<FocusSessionRecord> sessions) =>
        sessions.Sum(session => session.PlannedSeconds is > 0 ? session.ElapsedSeconds / session.PlannedSeconds.Value : 1d);

    public static int TotalMinutes(IEnumerable<FocusSessionRecord> sessions) =>
        (int)Math.Floor(sessions.Sum(session => session.ElapsedSeconds) / 60d);
}
