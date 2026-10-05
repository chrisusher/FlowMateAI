using API.Security;
using Microsoft.Azure.Functions.Worker.Http;
using Shared.Contracts;

namespace API.Functions.V1;

internal static class HttpFunction
{
    internal static async Task<(string? UserId, string? Email)> UserAsync(
        HttpRequestData request, OidcTokenValidator tokens, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        var principal = await tokens.ValidateAsync(authorization, cancellationToken);

        return (principal?.FindFirst("sub")?.Value, principal?.FindFirst("email")?.Value);
    }

    internal static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData request, HttpStatusCode status, string code, string message, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiError(code, message), cancellationToken);

        return response;
    }
}
