namespace Services.Database;

// Each collection uses its own container. Payloads retain the existing wire format.
public interface IWorkspaceItemDocument
{
    string Id { get; set; }
    string UserId { get; set; }
    string Payload { get; set; }
    int Position { get; set; }
    string? ETag { get; set; }
}

public sealed class WorkspaceProjectDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public int Position { get; set; }
    public string? ETag { get; set; }
}

public sealed class WorkspaceTaskDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public int Position { get; set; }
    public string? ETag { get; set; }
}

public sealed class WorkspaceSessionDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public int Position { get; set; }
    public string? ETag { get; set; }
}

public sealed class WorkspaceConversationDocument : IWorkspaceItemDocument
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public int Position { get; set; }
    public string? ETag { get; set; }
}
