namespace Services.Database;

public sealed class WorkspaceDocument
{
    public string Id { get; set; } = "workspace";
    public string UserId { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ETag { get; set; }
}
