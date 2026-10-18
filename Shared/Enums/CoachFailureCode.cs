namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<CoachFailureCode>))]
public enum CoachFailureCode
{
    [JsonStringEnumMemberName("quota-reached")]
    QuotaReached = 0,

    [JsonStringEnumMemberName("authentication-expired")]
    AuthenticationExpired = 1,

    [JsonStringEnumMemberName("sync-conflict")]
    SyncConflict = 2,

    [JsonStringEnumMemberName("provider-unavailable")]
    ProviderUnavailable = 3,

    [JsonStringEnumMemberName("timeout")]
    Timeout = 4
}
