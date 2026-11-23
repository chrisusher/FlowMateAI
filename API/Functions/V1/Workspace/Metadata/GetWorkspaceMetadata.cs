using API.Security;
using Services.Users;
using Services.Workspaces;

namespace API.Functions.V1.Workspace.Metadata;

public sealed class GetWorkspaceMetadata(Auth0TokenValidator tokens, IWorkspaceService workspaces, IUserService users)
{
    [Function(nameof(GetWorkspaceMetadata))]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/workspace/metadata")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (ownerId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var workspace = await workspaces.GetAsync(ownerId, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(WorkspaceMetadataResponse.Create(workspace), cancellationToken);

        return response;
    }
}
