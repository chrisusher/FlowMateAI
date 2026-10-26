using API.Security;
using ChrisUsher.Core.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Billing;
using Services.Users;
using Shared.Contracts;
using Services.ApiRequests;
using Shared.Enums;

namespace API.Functions.V1.Billing;

public sealed class BillingTrial(Auth0TokenValidator tokens, IBillingService billing, IUserService users, IAuthenticatedApiRateLimiter limiter)
{
    [Function("BillingTrial")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/billing/trial")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, email) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (userId is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        var rateLimitResponse = await HttpFunction.EnforceRateLimitAsync(request, limiter, userId, AuthenticatedApiRateLimitScope.BillingRequest, cancellationToken);

        if (rateLimitResponse is not null)
        {
            return rateLimitResponse;
        }

        var requestBody = await HttpFunction.ReadBoundedBodyAsync(request, 4096, cancellationToken);

        if (requestBody is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.RequestEntityTooLarge, "request_too_large", "Billing requests are limited to 4 KB.", cancellationToken);
        }

        CheckoutRequest? body;

        try
        {
            body = JsonSerializer.Deserialize<CheckoutRequest>(requestBody, SharedCommon.JsonOptions);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Choose monthly or annual billing.", cancellationToken);
        }

        var result = await billing.StartTrialAsync(userId, email, body.Annual, cancellationToken);
        var response = request.CreateResponse(result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(result, cancellationToken);

        return response;
    }
}
