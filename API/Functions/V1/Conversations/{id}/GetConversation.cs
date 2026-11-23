namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
        [Function("GetConversation")]
    public Task<HttpResponseData> GetConversation([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/conversations/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetConversationAsync, cancellationToken);
}
