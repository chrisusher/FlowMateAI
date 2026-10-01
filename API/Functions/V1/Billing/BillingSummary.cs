using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using API.Functions.V1;
using API.Security;
using Services.Billing;

namespace API.Functions.V1.Billing;

public sealed class BillingSummary(Auth0TokenValidator tokens, IBillingService billing)
{
    [Function("BillingSummary")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/billing")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, cancellationToken);

        if (userId is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(await billing.GetSummaryAsync(userId, cancellationToken), cancellationToken);

        return response;
    }
}
