using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Projects;

public sealed class GetProject(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("GetProject")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
    HttpFunction.GetAsync(request, tokens, users, id, items.GetProjectAsync, cancellationToken);
}
