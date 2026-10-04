namespace Shared.Enums;

public enum SubscriptionEntitlement
{
    [JsonStringEnumMemberName("not-entitled")]
    NotEntitled = 0,

    [JsonStringEnumMemberName("entitled")]
    Entitled = 1
}

public static class SubscriptionEntitlementExtensions
{
    public static SubscriptionEntitlement FromProviderStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "active" or "trialing" => SubscriptionEntitlement.Entitled,
        _ => SubscriptionEntitlement.NotEntitled
    };
}
