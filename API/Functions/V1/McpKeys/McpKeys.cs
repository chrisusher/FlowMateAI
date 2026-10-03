using System.Net;
using System.Text.Json;
using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Mcp;

namespace API.Functions.V1.McpKeys;

public sealed class McpKeys(Auth0TokenValidator tokens, IMcpCredentialService credentials)
{
    [Function("CreateMcpKey")]
    public async Task<HttpResponseData> Create([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/mcp-keys")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var owner = await OwnerAsync(request, cancellationToken);
        if (owner is null) return await Error(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        CreateRequest? body;
        try { body = await JsonSerializer.DeserializeAsync<CreateRequest>(request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken); }
        catch (JsonException) { body = null; }
        if (body is null || string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 80 || body.ExpiryDays is not (30 or 90 or 365))
            return await Error(request, HttpStatusCode.BadRequest, "invalid_request", "Provide a name up to 80 characters and an expiry of 30, 90, or 365 days.", cancellationToken);
        try
        {
            var created = await credentials.CreateAsync(owner, body.Name, body.ExpiryDays, cancellationToken);
            var response = request.CreateResponse(HttpStatusCode.Created);
            await response.WriteAsJsonAsync(new { created.Metadata.Id, created.Metadata.Name, created.Metadata.Prefix, created.Metadata.CreatedAt, created.Metadata.ExpiresAt, key = created.Key }, cancellationToken);
            return response;
        }
        catch (Exception)
        { return await Error(request, HttpStatusCode.ServiceUnavailable, "key_creation_failed", "The key could not be created. Try again.", cancellationToken); }
    }

    [Function("ListMcpKeys")]
    public async Task<HttpResponseData> List([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/mcp-keys")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var owner = await OwnerAsync(request, cancellationToken);
        if (owner is null) return await Error(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(await credentials.ListAsync(owner, cancellationToken), cancellationToken);
        return response;
    }

    [Function("RevokeMcpKey")]
    public async Task<HttpResponseData> Revoke([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/mcp-keys/{keyId}")] HttpRequestData request, string keyId, CancellationToken cancellationToken)
    {
        var owner = await OwnerAsync(request, cancellationToken);
        if (owner is null) return await Error(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        if (!await credentials.RevokeAsync(owner, keyId, cancellationToken)) return await Error(request, HttpStatusCode.NotFound, "not_found", "The key was not found.", cancellationToken);
        return request.CreateResponse(HttpStatusCode.NoContent);
    }

    private async Task<string?> OwnerAsync(HttpRequestData request, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        return (await tokens.ValidateAsync(authorization, cancellationToken))?.FindFirst("sub")?.Value;
    }
    private static async Task<HttpResponseData> Error(HttpRequestData req, HttpStatusCode status, string code, string message, CancellationToken token)
    { var response = req.CreateResponse(status); await response.WriteAsJsonAsync(new { code, message }, token); return response; }
    private sealed record CreateRequest(string Name, int ExpiryDays = 90);
}
