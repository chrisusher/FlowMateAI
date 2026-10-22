using System.Text.Json.Serialization;

namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<AccountDeletionStage>))]
public enum AccountDeletionStage
{
    [JsonStringEnumMemberName("billing")]
    Billing = 0,

    [JsonStringEnumMemberName("application-data")]
    ApplicationData = 1,

    [JsonStringEnumMemberName("identity")]
    Identity = 2,

    [JsonStringEnumMemberName("completed")]
    Completed = 3
}
