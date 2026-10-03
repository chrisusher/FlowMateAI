using API.Security;
using ChrisUsher.Core.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Workspaces;
using Shared.Contracts;

namespace API.Functions.V1.Workspace;

public sealed class Workspace(Auth0TokenValidator tokens, IWorkspaceService workspaces)
{

    [Function("Workspace")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "put", Route = "v1/workspace")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (ownerId, _) = await HttpFunction.UserAsync(request, tokens, cancellationToken);

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        if (request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = await workspaces.GetAsync(ownerId, cancellationToken);
            var response = request.CreateResponse(HttpStatusCode.OK);

            if (!string.IsNullOrEmpty(workspace.Revision))
            {
                response.Headers.Add("ETag", workspace.Revision);
            }

            await response.WriteAsJsonAsync(workspace, cancellationToken);

            return response;
        }
        WorkspaceSaveRequest? save;

        try
        {
            save = await JsonSerializer.DeserializeAsync<WorkspaceSaveRequest>(request.Body, SharedCommon.JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            save = null;
        }

        if (save?.Workspace is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Workspace data is missing or invalid.", cancellationToken);
        }

        var result = await workspaces.SaveAsync(ownerId, save, cancellationToken);

        if (!result.Succeeded)
        {
            var status = result.ErrorCode == "project_limit" ? HttpStatusCode.PaymentRequired : HttpStatusCode.Conflict;

            return await HttpFunction.ErrorAsync(request, status, result.ErrorCode ?? "save_failed", result.ErrorMessage ?? "The workspace could not be saved.", cancellationToken);
        }
        var saved = request.CreateResponse(HttpStatusCode.OK);

        if (!string.IsNullOrEmpty(result.Response!.Revision))
        {
            saved.Headers.Add("ETag", result.Response.Revision);
        }

        await saved.WriteAsJsonAsync(result.Response, cancellationToken);

        return saved;
    }
}
