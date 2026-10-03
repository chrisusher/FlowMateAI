namespace Shared.Enums;

public enum ColorTheme
{
    [JsonStringEnumMemberName("light")]
    Light = 0,

    [JsonStringEnumMemberName("dark")]
    Dark = 1
}

public static class ColorThemeExtensions
{
    public static string ToWireValue(this ColorTheme theme) => theme switch
    {
        ColorTheme.Light => "light",
        ColorTheme.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null)
    };
}
