using Shared.Models;

namespace Services.Database;

// Each collection uses its own container.
public interface IWorkspaceItemDocument
{
    string Id { get; set; }
    string UserId { get; set; }
    object? Value { get; }
    int Position { get; set; }
    string? ETag { get; set; }
}

public sealed class WorkspaceProjectDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public ProjectRecord? Project { get; set; }
    object? IWorkspaceItemDocument.Value => Project;
    public int Position { get; set; }
    public string? ETag { get; set; }
}

public sealed class WorkspaceTaskDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public TaskRecord? Task { get; set; }
    object? IWorkspaceItemDocument.Value => Task;
    public int Position { get; set; }
    public string? ETag { get; set; }
}

public sealed class WorkspaceSessionDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public FocusSessionRecord? Session { get; set; }
    object? IWorkspaceItemDocument.Value => Session;
    public int Position { get; set; }
    public string? ETag { get; set; }
}

public sealed class WorkspaceConversationDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public ConversationRecord? Conversation { get; set; }
    object? IWorkspaceItemDocument.Value => Conversation;
    public int Position { get; set; }
    public string? ETag { get; set; }
}
