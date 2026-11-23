using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.WorkspaceItems;

public sealed class ListConversations(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items) : WorkspaceItemFunction(tokens, users)
{
        [Function("ListConversations")]
    public Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/conversations")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListConversationsAsync, cancellationToken);
}
