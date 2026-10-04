using System.Net.Http.Headers;
using System.Net.Http.Json;
using Shared.Contracts;

namespace Web.Clients;

public sealed class McpKeysClient(HttpClient http, AuthClient auth)
{
    public async Task<IReadOnlyList<McpKeySummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var request = await auth.AuthorizedRequestAsync(HttpMethod.Get, "api/v1/mcp-keys", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);

        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<List<McpKeySummary>>(cancellationToken: cancellationToken) ?? [] : [];
    }

    public async Task<McpKeyCreatedResponse?> CreateAsync(string name, int expiryDays, CancellationToken cancellationToken = default)
    {
        using var request = await auth.AuthorizedRequestAsync(HttpMethod.Post, "api/v1/mcp-keys", cancellationToken);
        request.Content = JsonContent.Create(new McpKeyCreateRequest(name, expiryDays));
        using var response = await http.SendAsync(request, cancellationToken);

        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<McpKeyCreatedResponse>(cancellationToken: cancellationToken) : null;
    }

    public async Task<bool> RevokeAsync(string id, CancellationToken cancellationToken = default)
    {
        using var request = await auth.AuthorizedRequestAsync(HttpMethod.Delete, $"api/v1/mcp-keys/{Uri.EscapeDataString(id)}", cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);

        return response.IsSuccessStatusCode;
    }

}
