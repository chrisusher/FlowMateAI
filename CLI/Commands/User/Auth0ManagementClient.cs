using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shared.Exceptions;

namespace CLI.Commands.User;

public sealed class Auth0ManagementClient(HttpClient httpClient)
{
    public async Task<string> GetManagementTokenAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var authority = configuration["Auth0:Authority"]?.TrimEnd('/');
        var clientId = configuration["Auth0:ManagementClientId"];
        var clientSecret = configuration["Auth0:ManagementClientSecret"];

        if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) || authorityUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrWhiteSpace(authorityUri.AbsolutePath.Trim('/')))
        {
            throw new Auth0ManagementException("Auth0:Authority must be an HTTPS tenant authority URL.");
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new Auth0ManagementException("Auth0:ManagementClientId and Auth0:ManagementClientSecret are required.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(authorityUri, "/oauth/token"))
        {
            Content = JsonContent.Create(new
            {
                client_id = clientId,
                client_secret = clientSecret,
                audience = new Uri(authorityUri, "/api/v2/").ToString(),
                grant_type = "client_credentials"
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Auth0ManagementException($"Auth0 Management API token request failed (HTTP {(int)response.StatusCode}). Check the machine-to-machine client and its read:users grant.");
        }

        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var token = json.RootElement.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;

            return string.IsNullOrWhiteSpace(token)
                ? throw new Auth0ManagementException("Auth0 token response did not contain an access token.")
                : token;
        }
        catch (JsonException)
        {
            throw new Auth0ManagementException("Auth0 token response was invalid.");
        }
    }

    public async Task<IReadOnlyList<string>> FindUserIdsByEmailAsync(IConfiguration configuration, string email, CancellationToken cancellationToken)
    {
        var authority = configuration["Auth0:Authority"]!.TrimEnd('/') + "/";
        var token = await GetManagementTokenAsync(configuration, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(new Uri(authority), $"api/v2/users-by-email?email={Uri.EscapeDataString(email)}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Auth0ManagementException($"Auth0 user lookup failed (HTTP {(int)response.StatusCode}).");
        }

        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new Auth0ManagementException("Auth0 user lookup response was invalid.");
            }

            return json.RootElement.EnumerateArray()
                .Where(item => item.TryGetProperty("user_id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(item => item.GetProperty("user_id").GetString()!)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException)
        {
            throw new Auth0ManagementException("Auth0 user lookup response was invalid.");
        }
    }
}
