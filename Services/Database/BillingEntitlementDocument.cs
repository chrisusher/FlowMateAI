using Shared.Enums;

namespace Services.Database;

public sealed class BillingEntitlementDocument
{
    public string Id { get; set; } = "billing";
    public string UserId { get; set; } = "";
    public string StripeCustomerId { get; set; } = "";
    public string StripeSubscriptionId { get; set; } = "";
    public string Plan { get; set; } = BillingPlan.Free.ToString();
    public string SubscriptionStatus { get; set; } = "none";
    public bool TrialUsed { get; set; }
    public DateTimeOffset? PeriodEndsAt { get; set; }
    public string PromptMonth { get; set; } = "";
    public int PromptCount { get; set; }
    public string? ETag { get; set; }
}
