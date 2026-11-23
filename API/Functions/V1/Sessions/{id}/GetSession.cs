namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
        [Function("GetSession")]
    public Task<HttpResponseData> GetSession([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sessions/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetSessionAsync, cancellationToken);
}
