using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.WorkspaceItems;

public sealed class ListTasks(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items) : WorkspaceItemFunction(tokens, users)
{
        [Function("ListTasks")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListTasksAsync, cancellationToken);
}
