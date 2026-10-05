using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Billing;

namespace API.Functions.V1.Billing;

public sealed class BillingTrial(OidcTokenValidator tokens, IBillingService billing)
{
    [Function("BillingTrial")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/billing/trial")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, email) = await HttpFunction.UserAsync(request, tokens, cancellationToken);

        if (userId is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        var result = await billing.StartTrialAsync(userId, email, cancellationToken);
        var response = request.CreateResponse(result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(result, cancellationToken);

        return response;
    }
}
