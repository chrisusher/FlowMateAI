namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
        [Function("ListConversations")]
    public Task<HttpResponseData> ListConversations([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/conversations")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListConversationsAsync, cancellationToken);
}
