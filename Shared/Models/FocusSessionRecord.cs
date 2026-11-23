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

    [JsonIgnore]
    public double ElapsedSeconds => FocusSeconds ?? FocusMinutes * 60d;

    public static int TotalMinutes(IEnumerable<FocusSessionRecord> sessions) =>
        (int)Math.Floor(sessions.Sum(session => session.ElapsedSeconds) / 60d);
}
