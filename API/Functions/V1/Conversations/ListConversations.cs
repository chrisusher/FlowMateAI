using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.Conversations;

public sealed class ListConversations(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("ListConversations")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/conversations")] HttpRequestData request, CancellationToken cancellationToken) =>
    HttpFunction.ListAsync(request, tokens, users, items.ListConversationsAsync, cancellationToken);
}
