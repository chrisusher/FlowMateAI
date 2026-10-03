using Shared.Enums;

namespace Shared.Models;

public sealed class ChatMessageRecord
{
    [JsonConverter(typeof(JsonStringEnumConverter<ChatMessageRole>))]
    public ChatMessageRole Role { get; set; } = ChatMessageRole.Assistant;
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
