using System.Net.Http.Headers;

namespace Web.Clients;

public sealed class AccountClient(HttpClient http, Auth0Client auth)
{
    public async Task<ApiError?> DeleteAsync(string email, string confirmation, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, "api/v1/account")
        {
            Content = JsonContent.Create(new AccountDeletionRequest(confirmation, email))
        };

        if (!string.IsNullOrWhiteSpace(auth.Session.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        }

        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken: cancellationToken)
            ?? new ApiError("account_deletion_failed", "The account could not be deleted. Try again or contact support.");
    }
}
