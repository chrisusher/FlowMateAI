namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
        [Function("ListSessions")]
    public Task<HttpResponseData> ListSessions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sessions")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListSessionsAsync, cancellationToken);
}
