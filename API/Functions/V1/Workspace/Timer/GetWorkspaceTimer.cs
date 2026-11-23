using API.Security;
using Services.Users;
using Services.Workspaces;

namespace API.Functions.V1.Workspace.Timer;

public sealed class GetWorkspaceTimer(Auth0TokenValidator tokens, IWorkspaceService workspaces, IUserService users)
{
    [Function(nameof(GetWorkspaceTimer))]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/workspace/timer")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (ownerId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var workspace = await workspaces.GetAsync(ownerId, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(workspace.Workspace.Timer, cancellationToken);

        return response;
    }
}
