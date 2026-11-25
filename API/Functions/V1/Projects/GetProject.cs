using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Projects;

public sealed class GetProject(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items, IProjectWriteRepository writes)
{
    [Function("GetProject")]
    public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var versioned = await writes.GetAsync(userId, id, cancellationToken);
        var project = versioned.Rejection == ProjectChangeRejection.StorageNotReady
            ? await items.GetProjectAsync(userId, id, cancellationToken)
            : versioned.Project;

        if (project is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.NotFound, "not_found", "The record was not found.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);

        if (!string.IsNullOrWhiteSpace(versioned.Revision))
        {
            response.Headers.Add("ETag", versioned.Revision);
        }

        await response.WriteAsJsonAsync(project, cancellationToken);

        return response;
    }
}
