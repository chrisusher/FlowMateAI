using API.Security;
using ChrisUsher.Core.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Billing;
using Shared.Contracts;

namespace API.Functions.V1.Billing;

public sealed class BillingCheckout(Auth0TokenValidator tokens, IBillingService billing)
{

    [Function("BillingCheckout")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/billing/checkout")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, email) = await HttpFunction.UserAsync(request, tokens, cancellationToken);

        if (userId is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        CheckoutRequest? body;

        try
        {
            body = await JsonSerializer.DeserializeAsync<CheckoutRequest>(request.Body, SharedCommon.JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Choose monthly or annual billing.", cancellationToken);
        }

        var result = await billing.CreateCheckoutAsync(userId, email, body.Annual, cancellationToken);
        var response = request.CreateResponse(result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(result, cancellationToken);

        return response;
    }
}
