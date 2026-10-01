namespace Shared.Models;

public sealed class ProjectRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#5b68e8";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
