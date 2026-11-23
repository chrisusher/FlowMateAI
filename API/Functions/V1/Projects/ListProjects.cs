using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.WorkspaceItems;

public sealed class ListProjects(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items) : WorkspaceItemFunction(tokens, users)
{
    [Function("ListProjects")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListProjectsAsync, cancellationToken);
}
