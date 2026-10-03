namespace Shared.Enums;

public enum ChatMessageRole
{
    [JsonStringEnumMemberName("user")]
    User = 0,
    
    [JsonStringEnumMemberName("assistant")]
    Assistant = 1
}

public static class ChatMessageRoleExtensions
{
    public static string ToWireValue(this ChatMessageRole role) => role switch
    {
        ChatMessageRole.User => "user",
        ChatMessageRole.Assistant => "assistant",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null)
    };
}
