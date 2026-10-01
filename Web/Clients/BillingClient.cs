using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shared.Contracts;

namespace Web.Clients;

public sealed class BillingClient(HttpClient http, Auth0Client auth)
{
    public async Task<IReadOnlyList<BillingPrice>> GetPricesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/v1/billing/prices", cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];
        return await response.Content.ReadFromJsonAsync<List<BillingPrice>>(cancellationToken: cancellationToken) ?? [];
    }

    public async Task<BillingSummary?> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Get, "api/v1/billing");
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<BillingSummary>(cancellationToken: cancellationToken) : null;
    }

    public Task<BillingActionResponse> StartTrialAsync(CancellationToken cancellationToken = default) =>
        PostAsync("api/v1/billing/trial", null, cancellationToken);

    public Task<BillingActionResponse> CheckoutAsync(bool annual, CancellationToken cancellationToken = default) =>
        PostAsync("api/v1/billing/checkout", new CheckoutRequest(annual), cancellationToken);

    public Task<BillingActionResponse> PortalAsync(CancellationToken cancellationToken = default) =>
        PostAsync("api/v1/billing/portal", null, cancellationToken);

    private async Task<BillingActionResponse> PostAsync(string path, object? body, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Post, path);

        if (body is not null)
            request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, cancellationToken);
        return await response.Content.ReadFromJsonAsync<BillingActionResponse>(cancellationToken: cancellationToken)
            ?? new(false, null, "request_failed", "The billing request could not be completed.");
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);

        if (!string.IsNullOrWhiteSpace(auth.Session.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        return request;
    }
}
