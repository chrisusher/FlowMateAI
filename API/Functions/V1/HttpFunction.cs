using API.Security;
using Services.Users;
using Services.ApiRequests;
using System.Globalization;

namespace API.Functions.V1;

internal static class HttpFunction
{
    internal static async Task<HttpResponseData?> EnforceRateLimitAsync(
        HttpRequestData request,
        IAuthenticatedApiRateLimiter limiter,
        string userId,
        AuthenticatedApiRateLimitScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            var admission = await limiter.AdmitAsync(userId, scope, cancellationToken);

            if (admission.Admitted)
            {
                return null;
            }

            var response = await ErrorAsync(request, (HttpStatusCode)429, "rate_limited", "Too many requests. Wait briefly and try again.", cancellationToken);
            response.Headers.Add("Retry-After", admission.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture));

            return response;
        }
        catch (ApiRateLimitStorageUnavailableException)
        {
            return await ErrorAsync(request, HttpStatusCode.ServiceUnavailable, "rate_limit_unavailable", "Request protection is temporarily unavailable. Try again shortly.", cancellationToken);
        }
    }

    internal static async Task<byte[]?> ReadBoundedBodyAsync(HttpRequestData request, int maximumBytes, CancellationToken cancellationToken)
    {
        if (request.Headers.TryGetValues("Content-Length", out var values) &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var length) && length > maximumBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream(Math.Min(maximumBytes, 16 * 1024));
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await request.Body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, maximumBytes + 1 - (int)buffer.Length)), cancellationToken);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);

            if (buffer.Length > maximumBytes)
            {
                return null;
            }
        }
    }

    internal static async Task<(string? UserId, string? Email)> UserAsync(
        HttpRequestData request, Auth0TokenValidator tokens, IUserService users, CancellationToken cancellationToken)
    {
        var authorization = request.Headers.TryGetValues("Authorization", out var values) ? values.FirstOrDefault() : null;
        var principal = await tokens.ValidateAsync(authorization, cancellationToken);

        var userId = principal?.FindFirst("sub")?.Value;
        var email = principal?.FindFirst("email")?.Value;

        if (!string.IsNullOrWhiteSpace(userId) && !await AccountOperation.BeginAsync(request, userId, cancellationToken))
        {
            return (null, null);
        }

        var userAlreadyRegistered = false;

        if (!string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(email))
        {
            email = await users.FindEmailByUserIdAsync(userId, cancellationToken);
            userAlreadyRegistered = !string.IsNullOrWhiteSpace(email);

            if (string.IsNullOrWhiteSpace(email))
            {
                email = await tokens.GetEmailAsync(authorization, userId, cancellationToken);
            }
        }

        if (!userAlreadyRegistered && !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(email))
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

    internal static async Task<HttpResponseData> ListAsync<T>(HttpRequestData request, Auth0TokenValidator tokens, IUserService users, Func<string, CancellationToken, Task<IReadOnlyList<T>>> list, CancellationToken cancellationToken)
    {
        var (userId, _) = await UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var records = await list(userId, cancellationToken);
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(records, cancellationToken);

        return response;
    }

    internal static async Task<HttpResponseData> GetAsync<T>(HttpRequestData request, Auth0TokenValidator tokens, IUserService users, string id, Func<string, string, CancellationToken, Task<T?>> get, CancellationToken cancellationToken) where T : class
    {
        var (userId, _) = await UserAsync(request, tokens, users, cancellationToken);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await ErrorAsync(request, HttpStatusCode.Unauthorized, "unauthorized", "A valid FlowMate access token is required.", cancellationToken);
        }

        var record = await get(userId, id, cancellationToken);

        if (record is null)
        {
            return await ErrorAsync(request, HttpStatusCode.NotFound, "not_found", "The record was not found.", cancellationToken);
        }

        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(record, cancellationToken);

        return response;
    }
}
