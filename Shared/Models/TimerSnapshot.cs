using System.Security.Cryptography;
using System.Text;
using Shared.Enums;

namespace Shared.Models;

public sealed class TimerSnapshot
{
    public TimerPhase Phase { get; set; } = TimerPhase.Idle;
    public DateTimeOffset? EndsAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    public int RemainingSeconds { get; set; }
    public int DurationSeconds { get; set; }
    public List<FocusIntervalRecord> CompletedIntervals { get; set; } = [];
    public int CompletedPomodoros { get; set; }
    public string? FocusSessionId { get; set; }
    public string? TaskId { get; set; }
    public string ProjectId { get; set; } = "";
    public string OwnerClientId { get; set; } = "";

    public string EnsureFocusSessionId()
    {
        if (!string.IsNullOrWhiteSpace(FocusSessionId))
        {
            return FocusSessionId;
        }

        var startedAt = CompletedIntervals.OrderBy(interval => interval.StartedAt).FirstOrDefault()?.StartedAt ?? StartedAt ?? EndsAt;
        var identity = $"{startedAt?.ToUnixTimeMilliseconds()}|{EndsAt?.ToUnixTimeMilliseconds()}|{DurationSeconds}|{CompletedPomodoros}|{TaskId}|{ProjectId}";
        FocusSessionId = $"recovered-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))}";

        return FocusSessionId;
    }
}
