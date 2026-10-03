namespace Shared.Enums;

public enum BillingPlan
{
    [JsonStringEnumMemberName("free")]
    Free = 0,

    [JsonStringEnumMemberName("pro")]
    Pro = 1
}

public static class BillingPlanExtensions
{
    public static BillingPlan ParseOrFree(string? value) =>
        !int.TryParse(value, out _) && Enum.TryParse<BillingPlan>(value, ignoreCase: true, out var plan) && Enum.IsDefined(plan)
            ? plan
            : BillingPlan.Free;
}
