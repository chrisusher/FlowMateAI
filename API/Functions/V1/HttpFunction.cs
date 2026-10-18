using API.Security;
using Microsoft.Azure.Functions.Worker.Http;
using Shared.Contracts;
using Services.Users;

namespace API.Functions.V1;

internal static class HttpFunction
{
    internal static async Task<(string? UserId, string? Email)> UserAsync(
        HttpRequestData request, Auth0TokenValidator tokens, IUserService users, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        var principal = await tokens.ValidateAsync(authorization, cancellationToken);

        var userId = principal?.FindFirst("sub")?.Value;
        var email = principal?.FindFirst("email")?.Value;

        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(email))
        {
            await users.EnsureRegisteredAsync(userId, email, cancellationToken);
        }

        return (userId, email);
    }

    internal static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData request, HttpStatusCode status, string code, string message, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiError(code, message), cancellationToken);

        return response;
    }
}
