namespace Shared.Enums;

public enum BillingActionCode
{
    [JsonStringEnumMemberName("billing_unavailable")]
    BillingUnavailable = 0,

    [JsonStringEnumMemberName("trial_used")]
    TrialUsed = 1,

    [JsonStringEnumMemberName("stripe_error")]
    StripeError = 2,

    [JsonStringEnumMemberName("stripe_unavailable")]
    StripeUnavailable = 3,
    
    [JsonStringEnumMemberName("customer_missing")]
    CustomerMissing = 4,

    [JsonStringEnumMemberName("request_failed")]
    RequestFailed = 5
}
