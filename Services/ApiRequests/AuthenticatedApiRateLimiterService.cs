using Microsoft.Extensions.Configuration;
using Services.Database;
using Services.Repositories;
using Shared.Enums;

namespace Services.ApiRequests;

public sealed record ApiRateLimitAdmission(bool Admitted, int RetryAfterSeconds);

public interface IAuthenticatedApiRateLimiter
{
    Task<ApiRateLimitAdmission> AdmitAsync(string userId, AuthenticatedApiRateLimitScope scope, CancellationToken cancellationToken = default);
}

public sealed class AuthenticatedApiRateLimiterService(
    IMcpUsageRepository repository,
    IConfiguration configuration,
    TimeProvider clock) : IAuthenticatedApiRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public async Task<ApiRateLimitAdmission> AdmitAsync(string userId, AuthenticatedApiRateLimitScope scope, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || !Enum.IsDefined(scope))
        {
            throw new ArgumentException("A valid user and API request scope are required.");
        }

        var limit = Math.Clamp(configuration.GetValue($"ApiLimits:{scope}", DefaultLimit(scope)), 1, 1000);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var deadline = clock.GetUtcNow().AddSeconds(2);

            for (var attempt = 0; attempt < 5 && clock.GetUtcNow() < deadline; attempt++)
            {
                var now = clock.GetUtcNow();
                var usage = await repository.GetAsync(userId, timeout.Token);
                var admissions = (usage?.ApiAdmissions ?? [])
                    .Where(admission => admission.Scope == scope && admission.Timestamp > now - Window)
                    .OrderBy(admission => admission.Timestamp)
                    .ToList();

                if (admissions.Count >= limit)
                {
                    var retry = admissions[0].Timestamp + Window - now;

                    return new(false, Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)));
                }

                var retained = (usage?.ApiAdmissions ?? [])
                    .Where(admission => admission.Timestamp > now - Window)
                    .TakeLast(999)
                    .ToList();
                retained.Add(new ApiRateLimitRecord { Scope = scope, Timestamp = now });
                var updated = new McpUsageDocument
                {
                    UserId = userId,
                    UtcDay = usage?.UtcDay ?? "",
                    DailyCount = usage?.DailyCount ?? 0,
                    RecentAdmissions = usage?.RecentAdmissions ?? [],
                    ApiAdmissions = retained
                };

                if (await repository.TrySaveAsync(updated, usage?.ETag, timeout.Token))
                {
                    return new(true, 0);
                }
            }

            throw new ApiRateLimitStorageUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiRateLimitStorageUnavailableException();
        }
        catch (McpUsageStorageUnavailableException exception)
        {
            throw new ApiRateLimitStorageUnavailableException(exception);
        }
    }

    private static int DefaultLimit(AuthenticatedApiRateLimitScope scope) => scope switch
    {
        AuthenticatedApiRateLimitScope.WorkspaceWrite => 60,
        AuthenticatedApiRateLimitScope.CoachRequest => 5,
        AuthenticatedApiRateLimitScope.BillingRequest => 10,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
    };
}

public sealed class ApiRateLimitStorageUnavailableException(Exception? innerException = null)
    : Exception("Authenticated API rate-limit storage is temporarily unavailable.", innerException);
