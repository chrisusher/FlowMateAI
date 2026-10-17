using Newtonsoft.Json.Converters;

namespace Shared.Enums;

[Newtonsoft.Json.JsonConverter(typeof(StringEnumConverter))]
public enum EnvironmentType
{
    [JsonStringEnumMemberName("local")]
    Local = 0,

    [JsonStringEnumMemberName("development")]
    Development = 1,

    [JsonStringEnumMemberName("test")]
    Test = 2,

    [JsonStringEnumMemberName("production")]
    Production = 3,

    [JsonStringEnumMemberName("temp")]
    Temp = 4
}
