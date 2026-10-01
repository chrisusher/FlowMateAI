using Microsoft.Extensions.Configuration;

namespace CLI.Commands.User;

internal static class Auth0AccountResolver
{
    public static async Task<string> ResolveUserIdAsync(
        Auth0ManagementClient client,
        string email,
        string? requestedUserId,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new Auth0ManagementException("An email address is required.");
        }

        var ids = await client.FindUserIdsByEmailAsync(configuration, email, cancellationToken);

        return ResolveFromMatches(email, requestedUserId, ids);
    }

    internal static string ResolveFromMatches(string email, string? requestedUserId, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0)
        {
            throw new Auth0ManagementException($"No Auth0 account matched '{email}'.");
        }

        if (ids.Count > 1 && string.IsNullOrWhiteSpace(requestedUserId))
        {
            throw new Auth0ManagementException($"Multiple Auth0 accounts matched '{email}': {string.Join(", ", ids)}. Supply --user-id. No data was changed.");
        }

        if (!string.IsNullOrWhiteSpace(requestedUserId) && !ids.Contains(requestedUserId, StringComparer.Ordinal))
        {
            throw new Auth0ManagementException($"User ID '{requestedUserId}' is not among the accounts matched to '{email}'. No data was changed.");
        }

        return requestedUserId ?? ids[0];
    }
}
