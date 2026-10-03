namespace Shared.Enums;

public enum TaskPriority
{
    [JsonStringEnumMemberName("low")]
    Low = 0,

    [JsonStringEnumMemberName("medium")]
    Medium = 1,

    [JsonStringEnumMemberName("high")]
    High = 2
}
