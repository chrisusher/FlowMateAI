using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Mcp;
using Shared.Contracts;

namespace API.Functions.V1.McpKeys;

public sealed class CreateMcpKey(OidcTokenValidator tokens, IMcpCredentialService credentials)
{
    [Function(nameof(CreateMcpKey))]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/mcp-keys")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var owner = await McpKeyHttpHelpers.OwnerAsync(tokens, request, cancellationToken);

        if (owner is null)
        {
            return await McpKeyHttpHelpers.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        McpKeyCreateRequest? body;

        try
        {
            body = await JsonSerializer.DeserializeAsync<McpKeyCreateRequest>(request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 80 || body.ExpiryDays is not (30 or 90 or 365))
        {
            return await McpKeyHttpHelpers.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Provide a name up to 80 characters and an expiry of 30, 90, or 365 days.", cancellationToken);
        }

        try
        {
            var created = await credentials.CreateAsync(owner, body.Name, body.ExpiryDays, cancellationToken);
            var response = request.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new McpKeyCreatedResponse(
                created.Metadata.Id,
                created.Metadata.Name,
                created.Metadata.Prefix,
                created.Metadata.CreatedAt,
                created.Metadata.ExpiresAt,
                created.Key), cancellationToken);

            return response;
        }
        catch (Exception)
        {
            return await McpKeyHttpHelpers.ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "key_creation_failed", "The key could not be created. Try again.", cancellationToken);
        }
    }
}
