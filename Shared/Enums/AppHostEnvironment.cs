namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<AppHostEnvironment>))]
public enum AppHostEnvironment
{
    Local = 0,
    Test = 1
}
