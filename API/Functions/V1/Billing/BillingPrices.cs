using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Billing;

namespace API.Functions.V1.Billing;

public sealed class BillingPrices(OidcTokenValidator tokens, IBillingService billing)
{
    [Function("BillingPrices")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/billing/prices")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        try
        {
            var response = request.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(await billing.GetPricesAsync(cancellationToken), cancellationToken);

            return response;
        }
        catch (HttpRequestException)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "billing_unavailable", "Pricing is temporarily unavailable.", cancellationToken);
        }
    }
}
