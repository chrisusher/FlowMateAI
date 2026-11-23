using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Projects;

public sealed class ListProjects(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("ListProjects")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects")] HttpRequestData request, CancellationToken cancellationToken) =>
        HttpFunction.ListAsync(request, tokens, users, items.ListProjectsAsync, cancellationToken);
}
