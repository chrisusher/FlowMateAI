namespace Shared.Models;

public sealed class TaskRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public bool IsComplete { get; set; }
    public bool PlannedToday { get; set; }
    public int FocusMinutesToday { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
