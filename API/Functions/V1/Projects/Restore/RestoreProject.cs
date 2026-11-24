using API.Security;
using Services.ApiRequests;
using Services.Users;
using Services.Workspaces;

namespace API.Functions.V1.Projects.Restore;

public sealed class RestoreProject(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, IProjectWriteService projects)
{
    [Function("RestoreProject")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/projects/{id}/restore")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        ProjectWriteHttp.ExecuteAsync<ProjectStateRequest>(request, tokens, users, limiter,
            (userId, input, token) => projects.RestoreAsync(userId, id, input.Revision ?? "", token), HttpStatusCode.OK, cancellationToken);
}
