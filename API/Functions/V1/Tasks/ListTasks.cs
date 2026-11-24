using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Tasks;

public sealed class ListTasks(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("ListTasks")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks")] HttpRequestData request, CancellationToken cancellationToken) =>
    HttpFunction.ListAsync(request, tokens, users, items.ListTasksAsync, cancellationToken);
}
