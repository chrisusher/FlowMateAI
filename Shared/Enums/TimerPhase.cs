namespace Shared.Enums;

public enum TimerPhase
{
    [JsonStringEnumMemberName("idle")]
    Idle = 0,

    [JsonStringEnumMemberName("focus")]
    Focus = 1,

    [JsonStringEnumMemberName("short-break")]
    ShortBreak = 2,
    
    [JsonStringEnumMemberName("long-break")]
    LongBreak = 3,
    
    [JsonStringEnumMemberName("paused")]
    Paused = 4
}
