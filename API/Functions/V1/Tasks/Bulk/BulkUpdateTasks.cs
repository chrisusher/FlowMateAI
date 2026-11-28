using API.Security;
using ChrisUsher.Core.Shared;
using Services.ApiRequests;
using Services.Users;
using Services.Workspaces;
using Shared.Contracts;
using Shared.Enums;

namespace API.Functions.V1.Tasks.Bulk;

public sealed class BulkUpdateTasks(Auth0TokenValidator tokens, IUserService users, IAuthenticatedApiRateLimiter limiter, ITaskService tasks)
{
    [Function("BulkUpdateTasks")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/tasks/bulk")] HttpRequestData request, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var limited = await HttpFunction.EnforceRateLimitAsync(request, limiter, userId, AuthenticatedApiRateLimitScope.WorkspaceWrite, cancellationToken);

        if (limited is not null)
        {
            return limited;
        }

        var bytes = await HttpFunction.ReadBoundedBodyAsync(request, 65536, cancellationToken);

        if (bytes is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.RequestEntityTooLarge, "request_too_large", "Task data exceeds the request limit.", cancellationToken);
        }

        TaskBulkWriteRequest? input;

        try
        {
            input = JsonSerializer.Deserialize<TaskBulkWriteRequest>(bytes, SharedCommon.JsonOptions);
        }
        catch (JsonException)
        {
            input = null;
        }

        if (input?.Changes is not { Count: > 0 and <= 100 } || input.Changes.Any(change => change is null) ||
            input.Changes.Where(change => change.Task is not null).Select(change => change.Task!.Id).Distinct(StringComparer.Ordinal).Count() != input.Changes.Count)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Provide up to 100 distinct task changes.", cancellationToken);
        }

        var outcomes = new List<TaskBulkWriteOutcome>();

        foreach (var change in input.Changes)
        {
            var task = TaskWriteHttp.ToTask(change.Task);
            var result = task is null
                ? new Services.Repositories.TaskWriteResult(null, null, TaskChangeRejection.InvalidInput)
                : await tasks.UpdateAsync(userId, task, change.Revision ?? "", change.WorkspaceRevision ?? "", cancellationToken);

            if (result.Rejection == TaskChangeRejection.None && result.Task is not null && !string.IsNullOrWhiteSpace(result.Revision))
            {
                outcomes.Add(new(change.Task!.Id, new(result.Task, result.Revision, result.WorkspaceRevision, result.Timer), null, null));
            }
            else
            {
                var (_, code, message) = TaskWriteHttp.Error(result.Rejection);
                outcomes.Add(new(change.Task!.Id, null, code, message));
            }
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(outcomes, cancellationToken);

        return response;
    }
}
