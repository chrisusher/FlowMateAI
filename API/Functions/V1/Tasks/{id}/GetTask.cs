namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
        [Function("GetTask")]
    public Task<HttpResponseData> GetTask([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetTaskAsync, cancellationToken);
}
