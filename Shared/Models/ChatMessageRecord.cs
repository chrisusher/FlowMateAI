namespace Shared.Models;

public sealed class ChatMessageRecord
{
    public string Role { get; set; } = "assistant";
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
