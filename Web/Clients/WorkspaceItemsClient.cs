using System.Net.Http.Headers;
using ChrisUsher.Core.Shared;
using Shared.Contracts;
using System.Globalization;
using System.Text.Json;

namespace Web.Clients;

public sealed class WorkspaceItemsClient(HttpClient http, Auth0Client auth)
{
    public Task<List<ProjectRecord>> ListProjectsAsync(CancellationToken cancellationToken = default) => ListAsync<ProjectRecord>("projects", cancellationToken);
    public Task<List<TaskRecord>> ListTasksAsync(CancellationToken cancellationToken = default) => ListAsync<TaskRecord>("tasks", cancellationToken);
    public Task<List<FocusSessionRecord>> ListSessionsAsync(CancellationToken cancellationToken = default) => ListAsync<FocusSessionRecord>("sessions", cancellationToken);
    public Task<List<ConversationRecord>> ListConversationsAsync(CancellationToken cancellationToken = default) => ListAsync<ConversationRecord>("conversations", cancellationToken);
    public Task<TimerSnapshot?> GetTimerAsync(CancellationToken cancellationToken = default) => GetAsync<TimerSnapshot>("workspace", "timer", cancellationToken);
    public Task<ProjectRecord?> GetProjectAsync(string id, CancellationToken cancellationToken = default) => GetAsync<ProjectRecord>("projects", id, cancellationToken);
    public Task<TaskRecord?> GetTaskAsync(string id, CancellationToken cancellationToken = default) => GetAsync<TaskRecord>("tasks", id, cancellationToken);
    public Task<FocusSessionRecord?> GetSessionAsync(string id, CancellationToken cancellationToken = default) => GetAsync<FocusSessionRecord>("sessions", id, cancellationToken);
    public Task<ConversationRecord?> GetConversationAsync(string id, CancellationToken cancellationToken = default) => GetAsync<ConversationRecord>("conversations", id, cancellationToken);

    public async Task<ProjectWriteResponse?> GetProjectVersionedAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest($"api/v1/projects/{Uri.EscapeDataString(id)}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var project = await response.Content.ReadFromJsonAsync<ProjectRecord>(SharedCommon.JsonOptions, cancellationToken);
        var revision = response.Headers.TryGetValues("ETag", out var values) ? values.SingleOrDefault() : null;

        if (project is null || string.IsNullOrWhiteSpace(revision))
        {
            throw new InvalidDataException("The server did not return a project revision.");
        }

        return new(project, revision);
    }

    public async Task<TaskWriteResponse?> GetTaskVersionedAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest($"api/v1/tasks/{Uri.EscapeDataString(id)}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var task = await response.Content.ReadFromJsonAsync<TaskRecord>(SharedCommon.JsonOptions, cancellationToken);
        var revision = response.Headers.TryGetValues("ETag", out var values) ? values.SingleOrDefault() : null;

        if (task is null || string.IsNullOrWhiteSpace(revision))
        {
            throw new InvalidDataException("The server did not return a task revision.");
        }

        return new(task, revision);
    }

    public Task<TaskWriteResponse> CreateTaskAsync(TaskRecord task, string workspaceRevision, CancellationToken cancellationToken = default) =>
        WriteTaskAsync(HttpMethod.Post, "api/v1/tasks", new(task, null, workspaceRevision), cancellationToken);

    public Task<TaskWriteResponse> UpdateTaskAsync(TaskRecord task, string revision, string workspaceRevision, CancellationToken cancellationToken = default) =>
        WriteTaskAsync(HttpMethod.Put, $"api/v1/tasks/{Uri.EscapeDataString(task.Id)}", new(task, revision, workspaceRevision), cancellationToken);

    public Task<TaskWriteResponse> DeleteTaskAsync(string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default) =>
        WriteTaskAsync(HttpMethod.Delete, $"api/v1/tasks/{Uri.EscapeDataString(id)}", new(null, revision, workspaceRevision), cancellationToken);

    private async Task<TaskWriteResponse> WriteTaskAsync(HttpMethod method, string path, TaskWriteRequest payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(path, method);
        request.Content = JsonContent.Create(payload, options: SharedCommon.JsonOptions);
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<TaskWriteResponse>(SharedCommon.JsonOptions, cancellationToken);

        if (result?.Task is null || string.IsNullOrWhiteSpace(result.Revision))
        {
            throw new InvalidDataException("The server returned an incomplete task response.");
        }

        return result;
    }

    public Task<ProjectWriteResponse> CreateProjectAsync(ProjectCreateRequest project, CancellationToken cancellationToken = default) =>
        WriteProjectAsync(HttpMethod.Post, "api/v1/projects", project, cancellationToken);

    public Task<ProjectWriteResponse> RenameProjectAsync(string id, ProjectRenameRequest change, CancellationToken cancellationToken = default) =>
        WriteProjectAsync(HttpMethod.Put, $"api/v1/projects/{Uri.EscapeDataString(id)}", change, cancellationToken);

    public Task<ProjectWriteResponse> RestoreProjectAsync(string id, ProjectStateRequest change, CancellationToken cancellationToken = default) =>
        WriteProjectAsync(HttpMethod.Post, $"api/v1/projects/{Uri.EscapeDataString(id)}/restore", change, cancellationToken);

    public Task<ProjectWriteResponse> ArchiveProjectAsync(string id, ProjectArchiveRequest change, CancellationToken cancellationToken = default) =>
        WriteProjectAsync(HttpMethod.Post, $"api/v1/projects/{Uri.EscapeDataString(id)}/archive", change, cancellationToken);

    private async Task<ProjectWriteResponse> WriteProjectAsync<T>(HttpMethod method, string path, T payload, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(path, method);
        request.Content = JsonContent.Create(payload, options: SharedCommon.JsonOptions);
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ProjectWriteResponse>(SharedCommon.JsonOptions, cancellationToken);

        if (result?.Project is null || string.IsNullOrWhiteSpace(result.Revision))
        {
            throw new InvalidDataException("The server returned an incomplete project response.");
        }

        return result;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        ApiError? error = null;

        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiError>(SharedCommon.JsonOptions, cancellationToken);
        }
        catch (JsonException) { }

        throw new HttpRequestException(error?.Message ?? "The request could not be completed.", null, response.StatusCode);
    }

    public async Task<FocusTimeline> GetFocusTimelineAsync(DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var query = date is null ? "" : $"?date={date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var request = CreateRequest($"api/v1/reports/focus-timeline{query}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FocusTimeline>(new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
            ?? throw new InvalidDataException("The server returned an empty timeline response.");
    }

    public async Task<List<TaskFocus>> GetTaskFocusAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest($"api/v1/reports/task-focus?date={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<TaskFocus>>(new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
            ?? throw new InvalidDataException("The server returned an empty task focus response.");
    }

    private async Task<List<T>> ListAsync<T>(string collection, CancellationToken cancellationToken)
    {
        using var request = CreateRequest($"api/v1/{collection}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<T>>(SharedCommon.JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The server returned an empty collection response.");
    }

    private async Task<T?> GetAsync<T>(string collection, string id, CancellationToken cancellationToken) where T : class
    {
        using var request = CreateRequest($"api/v1/{collection}/{Uri.EscapeDataString(id)}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(SharedCommon.JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The server returned an empty record response.");
    }

    private HttpRequestMessage CreateRequest(string path, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);

        if (!string.IsNullOrWhiteSpace(auth.Session.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        }

        return request;
    }
}
