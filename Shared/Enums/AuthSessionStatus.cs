namespace Shared.Enums;

public enum AuthSessionStatus
{
    [JsonStringEnumMemberName("uninitialized")]
    Uninitialized = 0,

    [JsonStringEnumMemberName("unconfigured")]
    Unconfigured = 1,

    [JsonStringEnumMemberName("signed-out")]
    SignedOut = 2,

    [JsonStringEnumMemberName("signed-in")]
    SignedIn = 3,

    [JsonStringEnumMemberName("expired")]
    Expired = 4
}
