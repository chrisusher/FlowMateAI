using Microsoft.Extensions.Configuration;
using Shared.Exceptions;

namespace CLI.Commands.User;

internal static class KeycloakAccountResolver
{
    public static async Task<string> ResolveUserIdAsync(
        KeycloakManagementClient client,
        string email,
        string? requestedUserId,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new KeycloakManagementException("An email address is required.");
        }

        var ids = await client.FindUserIdsByEmailAsync(configuration, email, cancellationToken);

        return ResolveFromMatches(email, requestedUserId, ids);
    }

    internal static string ResolveFromMatches(string email, string? requestedUserId, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            throw new KeycloakManagementException($"No Keycloak account matched '{email}'.");
        }

        if (ids.Count > 1 && string.IsNullOrWhiteSpace(requestedUserId))
        {
            throw new KeycloakManagementException($"Multiple Keycloak accounts matched '{email}': {string.Join(", ", ids)}. Supply --user-id. No data was changed.");
        }

        if (!string.IsNullOrWhiteSpace(requestedUserId) && !ids.Contains(requestedUserId, StringComparer.Ordinal))
        {
            throw new KeycloakManagementException($"User ID '{requestedUserId}' is not among the accounts matched to '{email}'. No data was changed.");
        }

        return requestedUserId ?? ids[0];
    }
}
