using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Sessions;

public sealed class GetSession(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("GetSession")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sessions/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
    HttpFunction.GetAsync(request, tokens, users, id, items.GetSessionAsync, cancellationToken);
}
