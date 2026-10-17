using Shared.Contracts;

namespace Services.Billing;

public interface IBillingService
{
    Task<BillingSummary> GetSummaryAsync(string userId, CancellationToken cancellationToken = default);
    Task<(bool Allowed, int Used, int Limit)> ConsumePromptAsync(string userId, string month, CancellationToken cancellationToken = default);
    Task ReleasePromptAsync(string userId, string month, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingPrice>> GetPricesAsync(CancellationToken cancellationToken = default);
    Task<BillingActionResponse> StartTrialAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default);
    Task<BillingActionResponse> CreateCheckoutAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default, bool trial = false);
    Task<BillingActionResponse> CreatePortalAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> ProcessWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default);
}
