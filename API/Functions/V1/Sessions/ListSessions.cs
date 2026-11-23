using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Sessions;

public sealed class ListSessions(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("ListSessions")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sessions")] HttpRequestData request, CancellationToken cancellationToken) =>
    HttpFunction.ListAsync(request, tokens, users, items.ListSessionsAsync, cancellationToken);
}
