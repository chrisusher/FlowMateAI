using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.WorkspaceItems;

public sealed class GetTask(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items) : WorkspaceItemFunction(tokens, users)
{
        [Function("GetTask")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetTaskAsync, cancellationToken);
}
