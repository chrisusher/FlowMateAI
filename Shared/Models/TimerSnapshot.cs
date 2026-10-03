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
    public string? TaskId { get; set; }
    public string ProjectId { get; set; } = "";
    public string OwnerClientId { get; set; } = "";
}
