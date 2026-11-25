namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<AppHostEnvironment>))]
public enum AppHostEnvironment
{
    Local,
    Test
}
