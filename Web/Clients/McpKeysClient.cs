using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shared.Contracts;

namespace Web.Clients;

public sealed class McpKeysClient(HttpClient http, Auth0Client auth)
{
    public async Task<IReadOnlyList<McpKeySummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Get, "api/v1/mcp-keys");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<List<McpKeySummary>>(cancellationToken: cancellationToken) ?? [] : [];
    }

    public async Task<McpKeyCreatedResponse?> CreateAsync(string name, int expiryDays, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Post, "api/v1/mcp-keys");
        request.Content = JsonContent.Create(new McpKeyCreateRequest(name, expiryDays));
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<McpKeyCreatedResponse>(cancellationToken: cancellationToken) : null;
    }

    public async Task<bool> RevokeAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Delete, $"api/v1/mcp-keys/{Uri.EscapeDataString(id)}");
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        return response.IsSuccessStatusCode;
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);

        if (!string.IsNullOrWhiteSpace(auth.Session.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        }

        return request;
    }
}
