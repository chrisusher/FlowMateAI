using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Repositories;

namespace API.Functions.V1.Account;

public sealed class DeletionStatus(Auth0TokenValidator tokens, IAccountDeletionMarkerRepository markers)
{
    [Function(nameof(DeletionStatus))]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/account/deletion-status")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        var principal = await tokens.ValidateForDeletionAsync(authorization, cancellationToken);
        var userId = principal?.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid access token is required.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(await markers.IsMarkedAsync(userId, cancellationToken), cancellationToken);

        return response;
    }
}
