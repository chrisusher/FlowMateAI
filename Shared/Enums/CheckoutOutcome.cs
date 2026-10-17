namespace Shared.Enums;

public enum CheckoutOutcome
{
    [JsonStringEnumMemberName("return")]
    Return = 0,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled = 1
}
