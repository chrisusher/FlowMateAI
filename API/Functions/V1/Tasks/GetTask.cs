using API.Security;
using Services.Repositories;
using Services.Users;
using Shared.Enums;

namespace API.Functions.V1.Tasks;

public sealed class GetTask(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items, ITaskRepository writes)
{
    [Function("GetTask")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var versioned = await writes.GetAsync(userId, id, cancellationToken);
        var task = versioned.Rejection == TaskChangeRejection.StorageNotReady
            ? await items.GetTaskAsync(userId, id, cancellationToken)
            : versioned.Task;

        if (task is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.NotFound, "not_found", "The record was not found.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);

        if (!string.IsNullOrWhiteSpace(versioned.Revision))
        {
            response.Headers.Add("ETag", versioned.Revision);
        }

        await response.WriteAsJsonAsync(task, cancellationToken);

        return response;
    }
}
