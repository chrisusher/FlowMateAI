namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<SubscriptionStatus>))]
public enum SubscriptionStatus
{
    [JsonStringEnumMemberName("none")]
    None = 0,

    [JsonStringEnumMemberName("active")]
    Active = 1,

    [JsonStringEnumMemberName("trialing")]
    Trialing = 2,

    [JsonStringEnumMemberName("past_due")]
    PastDue = 3,

    [JsonStringEnumMemberName("canceled")]
    Canceled = 4,

    [JsonStringEnumMemberName("unpaid")]
    Unpaid = 5,

    [JsonStringEnumMemberName("incomplete")]
    Incomplete = 6,

    [JsonStringEnumMemberName("incomplete_expired")]
    IncompleteExpired = 7,

    [JsonStringEnumMemberName("paused")]
    Paused = 8,
    
    [JsonStringEnumMemberName("unknown")]
    Unknown = 9
}

public static class SubscriptionStatusExtensions
{
    public static SubscriptionStatus FromProviderStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "none" or null or "" => SubscriptionStatus.None,
        "active" => SubscriptionStatus.Active,
        "trialing" => SubscriptionStatus.Trialing,
        "past_due" => SubscriptionStatus.PastDue,
        "canceled" or "cancelled" => SubscriptionStatus.Canceled,
        "unpaid" => SubscriptionStatus.Unpaid,
        "incomplete" => SubscriptionStatus.Incomplete,
        "incomplete_expired" => SubscriptionStatus.IncompleteExpired,
        "paused" => SubscriptionStatus.Paused,
        _ => SubscriptionStatus.Unknown
    };
}
