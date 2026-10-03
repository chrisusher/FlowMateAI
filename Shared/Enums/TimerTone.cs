namespace Shared.Enums;

public enum TimerTone
{
    [JsonStringEnumMemberName("focus")]
    Focus = 0,

    [JsonStringEnumMemberName("break")]
    Break = 1
}

public static class TimerToneExtensions
{
    public static string ToWireValue(this TimerTone tone) => tone switch
    {
        TimerTone.Focus => "focus",
        TimerTone.Break => "break",
        _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, null)
    };
}
