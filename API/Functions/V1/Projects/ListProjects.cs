namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
    [Function("ListProjects")]
    public Task<HttpResponseData> ListProjects([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListProjectsAsync, cancellationToken);
}
