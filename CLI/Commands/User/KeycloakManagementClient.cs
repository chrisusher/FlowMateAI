using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shared.Exceptions;

namespace CLI.Commands.User;

public sealed class KeycloakManagementClient(HttpClient httpClient)
{
    private const int PageSize = 100;

    public async Task<string> GetManagementTokenAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var baseUri = GetBaseUri(configuration);
        var realm = Required(configuration, "Keycloak:Realm");
        var clientId = Required(configuration, "Keycloak:ManagementClientId");
        var clientSecret = Required(configuration, "Keycloak:ManagementClientSecret");
        var tokenUri = new Uri(baseUri, $"realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token");

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakManagementException($"Keycloak service-account token request failed (HTTP {(int)response.StatusCode}). Check the client-credentials service account and its query-users and view-users roles.");
        }

        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var token = json.RootElement.TryGetProperty("access_token", out var tokenElement) ? tokenElement.GetString() : null;

            return string.IsNullOrWhiteSpace(token)
                ? throw new KeycloakManagementException("Keycloak token response did not contain an access token.")
                : token;
        }
        catch (JsonException)
        {
            throw new KeycloakManagementException("Keycloak token response was invalid.");
        }
    }

    public async Task<IReadOnlyList<string>> FindUserIdsByEmailAsync(IConfiguration configuration, string email, CancellationToken cancellationToken)
    {
        var baseUri = GetBaseUri(configuration);
        var realm = Required(configuration, "Keycloak:Realm");
        var token = await GetManagementTokenAsync(configuration, cancellationToken);
        var usersUri = new Uri(baseUri, $"admin/realms/{Uri.EscapeDataString(realm)}/users");
        var exactMatches = new HashSet<string>(StringComparer.Ordinal);

        for (var first = 0; ; first += PageSize)
        {
            var separator = string.IsNullOrEmpty(usersUri.Query) ? "?" : "&";
            var pageUri = new Uri(usersUri + $"{separator}email={Uri.EscapeDataString(email)}&exact=true&first={first}&max={PageSize}");
            using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new KeycloakManagementException($"Keycloak user lookup failed (HTTP {(int)response.StatusCode}). Check query-users and view-users permissions.");
            }

            try
            {
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

                if (json.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new KeycloakManagementException("Keycloak user lookup response was invalid.");
                }

                var count = 0;

                foreach (var item in json.RootElement.EnumerateArray())
                {
                    count++;
                    var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                    var foundEmail = item.TryGetProperty("email", out var emailElement) ? emailElement.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(id) && string.Equals(foundEmail, email, StringComparison.OrdinalIgnoreCase))
                    {
                        exactMatches.Add(id);
                    }
                }

                if (count < PageSize)
                {
                    break;
                }
            }
            catch (JsonException)
            {
                throw new KeycloakManagementException("Keycloak user lookup response was invalid.");
            }
        }

        return exactMatches.Order(StringComparer.Ordinal).ToArray();
    }

    private static Uri GetBaseUri(IConfiguration configuration)
    {
        var value = Required(configuration, "Keycloak:Url").TrimEnd('/') + "/";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new KeycloakManagementException("Keycloak:Url must be an HTTPS server URL (HTTP is allowed for localhost development).");
        }

        return uri;
    }

    private static string Required(IConfiguration configuration, string key) =>
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new KeycloakManagementException($"{key} is required.");
}
