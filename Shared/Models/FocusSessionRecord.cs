namespace Shared.Models;

public sealed class FocusSessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? TaskId { get; set; }
    public string ProjectId { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public int FocusMinutes { get; set; }
}
