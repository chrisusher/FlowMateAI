namespace API.Functions.V1.WorkspaceItems;

public sealed partial class WorkspaceItems
{
        [Function("ListTasks")]
    public Task<HttpResponseData> ListTasks([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListTasksAsync, cancellationToken);
}
