using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Mcp;
using Shared.Contracts;

namespace API.Functions.V1.McpKeys;

public sealed class ListMcpKeys(Auth0TokenValidator tokens, IMcpCredentialService credentials)
{
    [Function(nameof(ListMcpKeys))]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/mcp-keys")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var owner = await McpKeyHttpHelpers.OwnerAsync(tokens, request, cancellationToken);

        if (owner is null)
        {
            return await McpKeyHttpHelpers.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        var keys = await credentials.ListAsync(owner, cancellationToken);
        await response.WriteAsJsonAsync(keys.Select(key => new McpKeySummary(
            key.Id,
            key.Name,
            key.Prefix,
            key.CreatedAt,
            key.ExpiresAt,
            key.Revoked)), cancellationToken);

        return response;
    }
}
