using API.Security;
using ChrisUsher.Core.Shared;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Services.Workspaces;
using Services.Users;
using Shared.Contracts;
using Shared.Enums;
using Services.ApiRequests;

namespace API.Functions.V1.Workspace;

public sealed class Workspace(Auth0TokenValidator tokens, IWorkspaceService workspaces, IUserService users, IAuthenticatedApiRateLimiter limiter)
{

    [Function("Workspace")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "put", Route = "v1/workspace")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (ownerId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

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

        var rateLimitResponse = await HttpFunction.EnforceRateLimitAsync(request, limiter, ownerId, AuthenticatedApiRateLimitScope.WorkspaceWrite, cancellationToken);

        if (rateLimitResponse is not null)
        {
            return rateLimitResponse;
        }

        var body = await HttpFunction.ReadBoundedBodyAsync(request, WorkspaceRequestValidator.MaximumPayloadBytes, cancellationToken);

        if (body is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.RequestEntityTooLarge, "request_too_large", "Workspace data exceeds the 2 MB request limit.", cancellationToken);
        }

        WorkspaceSaveRequest? save;

        try
        {
            save = JsonSerializer.Deserialize<WorkspaceSaveRequest>(body, SharedCommon.JsonOptions);
        }
        catch (JsonException)
        {
            save = null;
        }

        var validationError = WorkspaceRequestValidator.Validate(save?.Workspace);

        if (validationError is not null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", validationError, cancellationToken);
        }

        var result = await workspaces.SaveAsync(ownerId, save!, cancellationToken);

        if (!result.Succeeded)
        {
            var status = result.ErrorCode == WorkspaceSaveErrorCode.ProjectLimit ? HttpStatusCode.PaymentRequired : HttpStatusCode.Conflict;

            return await HttpFunction.ErrorAsync(request, status, ToErrorCode(result.ErrorCode), result.ErrorMessage ?? "The workspace could not be saved.", cancellationToken);
        }
        var saved = request.CreateResponse(HttpStatusCode.OK);

        if (!string.IsNullOrEmpty(result.Response!.Revision))
        {
            saved.Headers.Add("ETag", result.Response.Revision);
        }

        await saved.WriteAsJsonAsync(result.Response, cancellationToken);

        return saved;
    }

    private static string ToErrorCode(WorkspaceSaveErrorCode? code) => code switch
    {
        WorkspaceSaveErrorCode.ProjectLimit => "project_limit",
        WorkspaceSaveErrorCode.RevisionConflict => "revision_conflict",
        WorkspaceSaveErrorCode.TimerConflict => "timer_conflict",
        _ => "save_failed"
    };
}
