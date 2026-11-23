using API.Security;
using Services.Repositories;
using Services.Users;

namespace API.Functions.V1.WorkspaceItems;

public sealed class WorkspaceItems(Auth0TokenValidator tokens, IUserService users, IWorkspaceItemsRepository items)
{
    [Function("ListProjects")]
    public Task<HttpResponseData> ListProjects([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListProjectsAsync, cancellationToken);

    [Function("GetProject")]
    public Task<HttpResponseData> GetProject([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/projects/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetProjectAsync, cancellationToken);

    [Function("ListTasks")]
    public Task<HttpResponseData> ListTasks([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListTasksAsync, cancellationToken);

    [Function("GetTask")]
    public Task<HttpResponseData> GetTask([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/tasks/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetTaskAsync, cancellationToken);

    [Function("ListSessions")]
    public Task<HttpResponseData> ListSessions([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sessions")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListSessionsAsync, cancellationToken);

    [Function("GetSession")]
    public Task<HttpResponseData> GetSession([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/sessions/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetSessionAsync, cancellationToken);

    [Function("ListConversations")]
    public Task<HttpResponseData> ListConversations([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/conversations")] HttpRequestData request, CancellationToken cancellationToken) =>
        ListAsync(request, items.ListConversationsAsync, cancellationToken);

    [Function("GetConversation")]
    public Task<HttpResponseData> GetConversation([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/conversations/{id}")] HttpRequestData request, string id, CancellationToken cancellationToken) =>
        GetAsync(request, id, items.GetConversationAsync, cancellationToken);

    private async Task<HttpResponseData> ListAsync<T>(HttpRequestData request, Func<string, CancellationToken, Task<IReadOnlyList<T>>> list, CancellationToken cancellationToken)
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var records = await list(userId, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(records, cancellationToken);

        return response;
    }

    private async Task<HttpResponseData> GetAsync<T>(HttpRequestData request, string id, Func<string, string, CancellationToken, Task<T?>> get, CancellationToken cancellationToken) where T : class
    {
        var (userId, _) = await HttpFunction.UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var record = await get(userId, id, cancellationToken);

        if (record is null)
        {
            return await HttpFunction.ErrorAsync(request, HttpStatusCode.NotFound, "not_found", "The record was not found.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(record, cancellationToken);

        return response;
    }
}
