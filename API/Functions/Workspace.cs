using System.Net;
using System.Text.Json;
using API.Security;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Workspaces;
using Shared.Contracts;

namespace API.Functions;

public sealed class Workspace(Auth0TokenValidator tokens, IWorkspaceService workspaces)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Function("Workspace")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "put", Route = "v1/workspace")] HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        var principal = await tokens.ValidateAsync(authorization, cancellationToken);
        var ownerId = principal?.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(ownerId))
            return await ErrorAsync(request, HttpStatusCode.Unauthorized, new ApiError("unauthorized", "A valid FlowMate access token is required."), cancellationToken);

        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = await workspaces.GetAsync(ownerId, cancellationToken);
            var response = request.CreateResponse(HttpStatusCode.OK);
            if (!string.IsNullOrEmpty(workspace.Revision)) response.Headers.Add("ETag", workspace.Revision);
            await response.WriteAsJsonAsync(workspace, cancellationToken);
            return response;
        }

        WorkspaceSaveRequest? save;
        try { save = await JsonSerializer.DeserializeAsync<WorkspaceSaveRequest>(request.Body, JsonOptions, cancellationToken); }
        catch (JsonException) { save = null; }
        if (save?.Workspace is null)
            return await ErrorAsync(request, HttpStatusCode.BadRequest, new ApiError("invalid_request", "Workspace data is missing or invalid."), cancellationToken);

        var result = await workspaces.SaveAsync(ownerId, save, cancellationToken);
        if (!result.Succeeded)
        {
            var status = result.ErrorCode == "project_limit" ? HttpStatusCode.PaymentRequired : HttpStatusCode.Conflict;
            return await ErrorAsync(request, status, new ApiError(result.ErrorCode ?? "save_failed", result.ErrorMessage ?? "The workspace could not be saved."), cancellationToken);
        }

        var saved = request.CreateResponse(HttpStatusCode.OK);
        if (!string.IsNullOrEmpty(result.Response!.Revision)) saved.Headers.Add("ETag", result.Response.Revision);
        await saved.WriteAsJsonAsync(result.Response, cancellationToken);
        return saved;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData request, HttpStatusCode code, ApiError error, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(code);
        await response.WriteAsJsonAsync(error, cancellationToken);
        return response;
    }
}
