using API.Security;
using ChrisUsher.Core.Shared;
using Services.ApiRequests;
using Services.Repositories;
using Services.Users;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace API.Functions.V1.Tasks;

internal static class TaskWriteHttp
{
    public static async Task<HttpResponseData> ExecuteAsync<T>(HttpRequestData request, Auth0TokenValidator tokens, IUserService users,
        IAuthenticatedApiRateLimiter limiter, Func<string, T, CancellationToken, Task<TaskWriteResult>> write,
        HttpStatusCode successStatus, CancellationToken cancellationToken) where T : class
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

        var bytes = await HttpFunction.ReadBoundedBodyAsync(request, 8192, cancellationToken);

        if (bytes is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.RequestEntityTooLarge, "request_too_large", "Task data exceeds the request limit.", cancellationToken);
        }

        T? input;

        try
        {
            input = JsonSerializer.Deserialize<T>(bytes, SharedCommon.JsonOptions);
        }
        catch (JsonException)
        {
            input = null;
        }

        if (input is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Task data is missing or invalid.", cancellationToken);
        }

        var result = await write(userId, input, cancellationToken);

        if (result.Rejection != TaskChangeRejection.None || result.Task is null || string.IsNullOrWhiteSpace(result.Revision))
        {
            var (status, code, message) = Error(result.Rejection);

            return await HttpFunction.ErrorAsync(request, status, code, message, cancellationToken);
        }

        var response = request.CreateResponse(successStatus);
        await response.WriteAsJsonAsync(new TaskWriteResponse(result.Task, result.Revision, result.WorkspaceRevision, result.Timer), cancellationToken);

        return response;
    }

    public static TaskRecord? ToTask(TaskCreateData? task)
    {
        if (task?.Id is null || task.Title is null || task.ProjectId is null || task.Priority is null ||
            task.IsComplete is null || task.PlannedToday is null || task.FocusMinutesToday is null || task.CreatedAt is null)
        {
            return null;
        }

        return new TaskRecord
        {
            Id = task.Id,
            Title = task.Title,
            ProjectId = task.ProjectId,
            DueDate = task.DueDate,
            Priority = task.Priority.Value,
            IsComplete = task.IsComplete.Value,
            PlannedToday = task.PlannedToday.Value,
            FocusMinutesToday = task.FocusMinutesToday.Value,
            CreatedAt = task.CreatedAt.Value
        };
    }

    public static (HttpStatusCode Status, string Code, string Message) Error(TaskChangeRejection rejection) => rejection switch
    {
        TaskChangeRejection.InvalidInput => (HttpStatusCode.BadRequest, "invalid_request", "Task data is invalid."),
        TaskChangeRejection.NotFound => (HttpStatusCode.NotFound, "not_found", "The task was not found."),
        TaskChangeRejection.ProjectUnavailable => (HttpStatusCode.Conflict, "project_unavailable", "Choose an active project for this task."),
        TaskChangeRejection.TimerActive => (HttpStatusCode.Conflict, "timer_active", "Finish this task's timer before moving or removing it."),
        TaskChangeRejection.Capacity => (HttpStatusCode.BadRequest, "task_capacity", "The workspace has reached its task capacity."),
        TaskChangeRejection.StorageNotReady => (HttpStatusCode.ServiceUnavailable, "tasks_unavailable", "Task storage is not ready. Try again later."),
        _ => (HttpStatusCode.Conflict, "task_conflict", "The task changed or is no longer available. Reload and try again.")
    };
}
