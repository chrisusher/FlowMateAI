using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Billing;

namespace API.Functions.V1.Billing;

public sealed class BillingPrices(IBillingService billing)
{
    [Function("BillingPrices")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/billing/prices")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
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
