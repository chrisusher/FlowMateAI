using API.Security;
using Services.ApiRequests;
using Services.Repositories;
using Services.Users;
using Services.Workspaces;
using Shared.Contracts;
using Shared.Enums;

namespace API.Functions.V1.Tasks;

public sealed class UpdateTask(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, ITaskService tasks)
{
    [Function("UpdateTask")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "v1/tasks/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        TaskWriteHttp.ExecuteAsync<TaskCreateRequest>(request, tokens, users, limiter,
            (userId, input, token) => UpdateAsync(userId, id, input, token), HttpStatusCode.OK, cancellationToken);

    private Task<TaskWriteResult> UpdateAsync(string userId, string id, TaskCreateRequest input, CancellationToken cancellationToken)
    {
        var task = TaskWriteHttp.ToTask(input.Task);

        return task is null || task.Id != id
            ? Task.FromResult(new TaskWriteResult(null, null, TaskChangeRejection.InvalidInput))
            : tasks.UpdateAsync(userId, task, input.Revision ?? "", input.WorkspaceRevision ?? "", cancellationToken);
    }
}
