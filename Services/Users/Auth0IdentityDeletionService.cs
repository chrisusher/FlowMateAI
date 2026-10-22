using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shared.Exceptions;

namespace Services.Users;

public sealed class Auth0IdentityDeletionService(IConfiguration configuration, IHttpClientFactory clients) : IAuth0IdentityDeletionService
{
    public bool IsConfigured =>
        IsValidAuthority(configuration["Auth0:Authority"]) &&
        !string.IsNullOrWhiteSpace(configuration["Auth0:ManagementClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["Auth0:ManagementClientSecret"]);

    public async Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!IsConfigured)
        {
            throw new Auth0ManagementException("Auth0 Management API deletion is not configured.");
        }

        var authority = configuration["Auth0:Authority"]!.TrimEnd('/') + "/";
        var token = await GetManagementTokenAsync(authority, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(new Uri(authority), $"api/v2/users/{Uri.EscapeDataString(userId)}"));
        request.Headers.Authorization = new("Bearer", token);

        using var response = await clients.CreateClient().SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        throw new Auth0ManagementException($"Auth0 identity deletion failed (HTTP {(int)response.StatusCode}).");
    }

    private async Task<string> GetManagementTokenAsync(string authority, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(authority), "oauth/token"))
        {
            Content = JsonContent.Create(new
            {
                grant_type = "client_credentials",
                client_id = configuration["Auth0:ManagementClientId"],
                client_secret = configuration["Auth0:ManagementClientSecret"],
                audience = new Uri(new Uri(authority), "api/v2/").ToString()
            })
        };
        using var response = await clients.CreateClient().SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Auth0ManagementException($"Auth0 Management API token request failed (HTTP {(int)response.StatusCode}).");
        }

        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

            return body.TryGetProperty("access_token", out var accessToken)
                && accessToken.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(accessToken.GetString())
                    ? accessToken.GetString()!
                    : throw new Auth0ManagementException("Auth0 Management API token response did not contain an access token.");
        }
        catch (JsonException)
        {
            throw new Auth0ManagementException("Auth0 Management API token response was invalid.");
        }
    }

    private static bool IsValidAuthority(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var authority)
        && authority.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(authority.Host);
}
