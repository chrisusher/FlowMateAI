using Shared.Enums;

namespace Shared.Contracts;

public sealed record BillingSummary(string Plan, string Status, bool TrialUsed, DateTimeOffset? PeriodEndsAt, int PromptsUsed, int PromptLimit, BillingInterval? SubscriptionInterval = null, bool CancelAtPeriodEnd = false, bool IsManualGrant = false);
