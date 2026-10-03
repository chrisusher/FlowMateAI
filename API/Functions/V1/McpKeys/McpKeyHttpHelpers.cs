using API.Security;
using Microsoft.Azure.Functions.Worker.Http;
using Shared.Contracts;

namespace API.Functions.V1.McpKeys;

internal static class McpKeyHttpHelpers
{
    public static async Task<string?> OwnerAsync(Auth0TokenValidator tokens, HttpRequestData request, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;

        return (await tokens.ValidateAsync(authorization, cancellationToken))?.FindFirst("sub")?.Value;
    }

    public static async Task<HttpResponseData> ErrorAsync(HttpRequestData request, HttpStatusCode status, string code, string message, CancellationToken cancellationToken)
    {
        var response = request.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiError(code, message), cancellationToken);

        return response;
    }
}
