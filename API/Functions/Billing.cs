using System.Net;
using System.Text.Json;
using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Billing;
using Shared.Contracts;

namespace API.Functions;

public sealed class Billing(Auth0TokenValidator tokens, IBillingService billing)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("BillingPrices")]
    public async Task<HttpResponseData> Prices([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/billing/prices")] HttpRequestData request, CancellationToken cancellationToken)
    {
        try
        {
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(await billing.GetPricesAsync(cancellationToken), cancellationToken);
            return response;
        }
        catch (HttpRequestException)
        {
            return await ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "billing_unavailable", "Pricing is temporarily unavailable.", cancellationToken);
        }
    }

    [Function("BillingSummary")]
    public async Task<HttpResponseData> Summary([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/billing")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, email) = await UserAsync(request, cancellationToken);
        if (userId is null) return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        var summary = await billing.GetSummaryAsync(userId, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(summary, cancellationToken);
        return response;
    }

    [Function("BillingTrial")]
    public async Task<HttpResponseData> StartTrial([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/billing/trial")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, email) = await UserAsync(request, cancellationToken);
        if (userId is null) return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        var result = await billing.StartTrialAsync(userId, email, cancellationToken);
        return await ActionResponseAsync(request, result, cancellationToken);
    }

    [Function("BillingCheckout")]
    public async Task<HttpResponseData> Checkout([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/billing/checkout")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, email) = await UserAsync(request, cancellationToken);
        if (userId is null) return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        CheckoutRequest? body;
        try { body = await JsonSerializer.DeserializeAsync<CheckoutRequest>(request.Body, JsonOptions, cancellationToken); }
        catch (JsonException) { body = null; }
        if (body is null) return await ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Choose monthly or annual billing.", cancellationToken);
        return await ActionResponseAsync(request, await billing.CreateCheckoutAsync(userId, email, body.Annual, cancellationToken), cancellationToken);
    }

    [Function("BillingPortal")]
    public async Task<HttpResponseData> Portal([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/billing/portal")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, _) = await UserAsync(request, cancellationToken);
        if (userId is null) return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        return await ActionResponseAsync(request, await billing.CreatePortalAsync(userId, cancellationToken), cancellationToken);
    }

    private async Task<(string? UserId, string? Email)> UserAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        var principal = await tokens.ValidateAsync(authorization, cancellationToken);
        return (principal?.FindFirst("sub")?.Value, principal?.FindFirst("email")?.Value);
    }

    private static async Task<HttpResponseData> ActionResponseAsync(HttpRequestData request, BillingActionResponse result, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(result, cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData request, HttpStatusCode status, string code, string message, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiError(code, message), cancellationToken);
        return response;
    }
}
