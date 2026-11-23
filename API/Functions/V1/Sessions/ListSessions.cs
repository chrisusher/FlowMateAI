using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.WorkspaceItems;

public sealed class ListSessions(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items) : WorkspaceItemFunction(tokens, users)
{
        [Function("ListSessions")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sessions")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListSessionsAsync, cancellationToken);
}
