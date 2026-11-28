using API.Security;
using Services.ApiRequests;
using Services.Users;
using Services.Workspaces;
using Shared.Contracts;

namespace API.Functions.V1.Tasks;

public sealed class DeleteTask(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, ITaskService tasks)
{
    [Function("DeleteTask")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "v1/tasks/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        TaskWriteHttp.ExecuteAsync<TaskWriteRequest>(request, tokens, users, limiter,
            (userId, input, token) => tasks.DeleteAsync(userId, id, input.Revision ?? "", input.WorkspaceRevision ?? "", token), HttpStatusCode.OK, cancellationToken);
}
