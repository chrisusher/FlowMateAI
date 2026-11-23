namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
        [Function("GetProject")]
    public Task<HttpResponseData> GetProject([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetProjectAsync, cancellationToken);
}
