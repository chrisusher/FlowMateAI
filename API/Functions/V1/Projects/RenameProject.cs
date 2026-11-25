using API.Security;
using Services.ApiRequests;
using Services.Users;
using Services.Workspaces;

namespace API.Functions.V1.Projects;

public sealed class RenameProject(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, IProjectWriteService projects)
{
    [Function("RenameProject")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/projects/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        ProjectWriteHttp.ExecuteAsync<ProjectRenameRequest>(request, tokens, users, limiter,
            (userId, input, token) => projects.RenameAsync(userId, id, input.Name, input.Revision ?? "", token), HttpStatusCode.OK, cancellationToken);
}
