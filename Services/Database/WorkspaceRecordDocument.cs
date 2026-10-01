using System.Text.Json;
using Shared.Models;

namespace Services.Database;

public sealed class WorkspaceRecordDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string RecordType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Color { get; set; }
    public string? ProjectId { get; set; }
    public string? TaskId { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public bool? IsComplete { get; set; }
    public bool? PlannedToday { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? FocusMinutes { get; set; }
    public string Payload { get; set; } = "{}";

    public static IEnumerable<WorkspaceRecordDocument> FromSnapshot(string userId, WorkspaceSnapshot workspace)
    {
        foreach (var project in workspace.Projects)
            yield return new() { Id = $"project:{project.Id}", UserId = userId, RecordType = "project", ProjectId = project.Id, Title = project.Name, Color = project.Color, Payload = JsonSerializer.Serialize(project) };
        foreach (var task in workspace.Tasks)
            yield return new() { Id = $"task:{task.Id}", UserId = userId, RecordType = "task", TaskId = task.Id, ProjectId = task.ProjectId, Title = task.Title, DueDate = task.DueDate, Priority = task.Priority.ToString(), IsComplete = task.IsComplete, PlannedToday = task.PlannedToday, Payload = JsonSerializer.Serialize(task) };
        foreach (var session in workspace.Sessions)
            yield return new() { Id = $"focus:{session.Id}", UserId = userId, RecordType = "focus_session", TaskId = session.TaskId, ProjectId = session.ProjectId, StartedAt = session.StartedAt, EndedAt = session.EndedAt, FocusMinutes = session.FocusMinutes, Payload = JsonSerializer.Serialize(session) };
        foreach (var conversation in workspace.Conversations)
            yield return new() { Id = $"conversation:{conversation.Id}", UserId = userId, RecordType = "conversation", Title = conversation.Title, StartedAt = conversation.Messages.FirstOrDefault()?.CreatedAt ?? conversation.UpdatedAt, EndedAt = conversation.UpdatedAt, Payload = JsonSerializer.Serialize(conversation) };
        yield return new() { Id = "timer", UserId = userId, RecordType = "timer", Status = workspace.Timer.Phase.ToString(), TaskId = workspace.Timer.TaskId, ProjectId = workspace.Timer.ProjectId, Payload = JsonSerializer.Serialize(workspace.Timer) };
    }
}
