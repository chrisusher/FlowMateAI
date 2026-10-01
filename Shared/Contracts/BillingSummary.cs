namespace Shared.Contracts;

public sealed record BillingSummary(string Plan, string Status, bool TrialUsed, DateTimeOffset? PeriodEndsAt, int PromptsUsed, int PromptLimit);
