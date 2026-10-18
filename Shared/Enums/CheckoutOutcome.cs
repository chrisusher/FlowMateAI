namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<CheckoutOutcome>))]
public enum CheckoutOutcome
{
    [JsonStringEnumMemberName("return")]
    Return = 0,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled = 1,

    [JsonStringEnumMemberName("trial-return")]
    TrialReturn = 2,

    [JsonStringEnumMemberName("trial-cancelled")]
    TrialCancelled = 3
}
