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

    public async Task<FocusTimeline> GetFocusTimelineAsync(DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var query = date is null ? "" : $"?date={date.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var request = CreateRequest($"api/v1/reports/focus-timeline{query}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FocusTimeline>(new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
            ?? throw new InvalidDataException("The server returned an empty timeline response.");
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

    private HttpRequestMessage CreateRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);

        if (!string.IsNullOrWhiteSpace(auth.Session.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        }

        return request;
    }
}
