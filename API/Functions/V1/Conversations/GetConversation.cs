using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Conversations;

public sealed class GetConversation(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("GetConversation")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/conversations/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
    HttpFunction.GetAsync(request, tokens, users, id, items.GetConversationAsync, cancellationToken);
}
