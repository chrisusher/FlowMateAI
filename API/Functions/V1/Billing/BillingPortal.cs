using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Billing;

namespace API.Functions.V1.Billing;

public sealed class BillingPortal(Auth0TokenValidator tokens, IBillingService billing)
{
    [Function("BillingPortal")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/billing/portal")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, cancellationToken);

        if (userId is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        var result = await billing.CreatePortalAsync(userId, cancellationToken);
        var response = request.CreateResponse(result.Succeeded ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(result, cancellationToken);

        return response;
    }
}
