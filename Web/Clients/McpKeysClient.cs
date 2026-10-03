using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Web.Clients;

public sealed record McpKeyItem(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Revoked);
public sealed record McpKeyCreated(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, string Key);

public sealed class McpKeysClient(HttpClient http, Auth0Client auth)
{
    public async Task<IReadOnlyList<McpKeyItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Get, "api/v1/mcp-keys");
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<List<McpKeyItem>>(cancellationToken: cancellationToken) ?? [] : [];
    }
    public async Task<McpKeyCreated?> CreateAsync(string name, int expiryDays, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Post, "api/v1/mcp-keys");
        request.Content = JsonContent.Create(new { name, expiryDays });
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<McpKeyCreated>(cancellationToken: cancellationToken) : null;
    }
    public async Task<bool> RevokeAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = Authorized(HttpMethod.Delete, $"api/v1/mcp-keys/{Uri.EscapeDataString(id)}");
        using var response = await http.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }
    private HttpRequestMessage Authorized(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(auth.Session.AccessToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        return request;
    }
}
