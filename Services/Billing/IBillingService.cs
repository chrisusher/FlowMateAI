using Shared.Contracts;

namespace Services.Billing;

public interface IBillingService
{
    Task<BillingSummary> GetSummaryAsync(string userId, CancellationToken cancellationToken = default);
    Task<(bool Allowed, int Used, int Limit)> ConsumePromptAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BillingPrice>> GetPricesAsync(CancellationToken cancellationToken = default);
    Task<BillingActionResponse> StartTrialAsync(string userId, string? email, CancellationToken cancellationToken = default);
    Task<BillingActionResponse> CreateCheckoutAsync(string userId, string? email, bool annual, CancellationToken cancellationToken = default);
    Task<BillingActionResponse> CreatePortalAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> ProcessWebhookAsync(string payload, string? signature, CancellationToken cancellationToken = default);
}
