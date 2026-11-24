using API.Security;
using ChrisUsher.Core.Shared;
using Services.ApiRequests;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Projects;

internal static class ProjectWriteHttp
{
    public static async Task<HttpResponseData> ExecuteAsync<T>(HttpRequestData request, Auth0TokenValidator tokens, IUserService users,
        IAuthenticatedApiRateLimiter limiter, Func<string, T, CancellationToken, Task<ProjectWriteResult>> write,
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
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.RequestEntityTooLarge, "request_too_large", "Project data exceeds the request limit.", cancellationToken);
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
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.BadRequest, "invalid_request", "Project data is missing or invalid.", cancellationToken);
        }

        var result = await write(userId, input, cancellationToken);

        if (result.Rejection != ProjectChangeRejection.None || result.Project is null || string.IsNullOrWhiteSpace(result.Revision))
        {
            var (status, code, message) = result.Rejection switch
            {
                ProjectChangeRejection.InvalidInput => (HttpStatusCode.BadRequest, "invalid_request", "Project data is invalid."),
                ProjectChangeRejection.NotFound => (HttpStatusCode.NotFound, "not_found", "The project was not found."),
                ProjectChangeRejection.ProjectLimit => (HttpStatusCode.PaymentRequired, "project_limit", "Your plan's active project limit has been reached."),
                ProjectChangeRejection.TimerActive => (HttpStatusCode.Conflict, "timer_active", "Finish this project's timer before archiving it."),
                ProjectChangeRejection.Capacity => (HttpStatusCode.BadRequest, "project_capacity", "The workspace has reached its project capacity."),
                ProjectChangeRejection.StorageNotReady => (HttpStatusCode.ServiceUnavailable, "projects_unavailable", "Project storage is not ready. Try again later."),
                _ => (HttpStatusCode.Conflict, "project_conflict", "The project changed or is no longer available. Reload and try again.")
            };

            return await HttpFunction.ErrorAsync(request, status, code, message, cancellationToken);
        }

        var response = request.CreateResponse(successStatus);
        await response.WriteAsJsonAsync(new ProjectWriteResponse(result.Project, result.Revision, result.WorkspaceRevision, result.Timer), cancellationToken);

        return response;
    }
}
