using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Tasks;

public sealed class GetTask(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("GetTask")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
    HttpFunction.GetAsync(request, tokens, users, id, items.GetTaskAsync, cancellationToken);
}
