using API.Security;
using Services.ApiRequests;
using Services.Users;
using Services.Workspaces;

namespace API.Functions.V1.Projects.Archive;

public sealed class ArchiveProject(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, IProjectWriteService projects)
{
    [Function("ArchiveProject")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/projects/{id}/archive")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        ProjectWriteHttp.ExecuteAsync<ProjectArchiveRequest>(request, tokens, users, limiter,
            (userId, input, token) => projects.ArchiveAsync(userId, id, input.Revision ?? "", input.WorkspaceRevision ?? "", token), HttpStatusCode.OK, cancellationToken);
}
