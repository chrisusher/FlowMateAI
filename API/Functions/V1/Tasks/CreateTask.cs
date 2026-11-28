using API.Security;
using Services.ApiRequests;
using Services.Repositories;
using Services.Users;
using Services.Workspaces;
using Shared.Contracts;
using Shared.Enums;

namespace API.Functions.V1.Tasks;

public sealed class CreateTask(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, ITaskService tasks)
{
    [Function("CreateTask")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/tasks")] HttpRequestData request, CancellationToken cancellationToken) =>
        TaskWriteHttp.ExecuteAsync<TaskCreateRequest>(request, tokens, users, limiter, CreateAsync, HttpStatusCode.Created, cancellationToken);

    private Task<TaskWriteResult> CreateAsync(string userId, TaskCreateRequest input, CancellationToken cancellationToken)
    {
        var task = TaskWriteHttp.ToTask(input.Task);

        return task is null
            ? Task.FromResult(new TaskWriteResult(null, null, TaskChangeRejection.InvalidInput))
            : tasks.CreateAsync(userId, task, input.WorkspaceRevision ?? "", cancellationToken);
    }
}
