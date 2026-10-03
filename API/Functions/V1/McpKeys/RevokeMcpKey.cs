using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Mcp;

namespace API.Functions.V1.McpKeys;

public sealed class RevokeMcpKey(Auth0TokenValidator tokens, IMcpCredentialService credentials)
{
    [Function(nameof(RevokeMcpKey))]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/mcp-keys/{keyId}")] HttpRequestData request, string keyId, CancellationToken cancellationToken)
    {
        var owner = await McpKeyHttpHelpers.OwnerAsync(tokens, request, cancellationToken);

        if (owner is null)
        {
            return await McpKeyHttpHelpers.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        if (!await credentials.RevokeAsync(owner, keyId, cancellationToken))
        {
            return await McpKeyHttpHelpers.ErrorAsync(request, HttpStatusCode.NotFound, "not_found", "The key was not found.", cancellationToken);
        }

        return request.CreateResponse(HttpStatusCode.NoContent);
    }
}
