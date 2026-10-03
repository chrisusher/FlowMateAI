using Microsoft.Extensions.Configuration;
using Services.Database;
using Services.Repositories;

namespace Services.Mcp;

public sealed record FreeAdmission(bool Admitted, int PerMinuteLimit, int PerDayLimit, int MinuteRemaining, int DayRemaining, int RetryAfterSeconds, DateTimeOffset MinuteResetAt, DateTimeOffset DayResetAt);

public interface IFreeUsageLimiterService
{
    int PerMinuteLimit { get; }
    int PerDayLimit { get; }
    Task<FreeAdmission> AdmitAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class FreeUsageLimiterService(IMcpUsageRepository repository, IConfiguration configuration, TimeProvider clock) : IFreeUsageLimiterService
{
    public int PerMinuteLimit { get; } = Math.Max(1, configuration.GetValue("Mcp:FreePerMinute", 10));
    public int PerDayLimit { get; } = Math.Max(1, configuration.GetValue("Mcp:FreePerDay", 100));

    public async Task<FreeAdmission> AdmitAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var deadline = clock.GetUtcNow().AddSeconds(2);

            for (var attempt = 0; attempt < 5 && clock.GetUtcNow() < deadline; attempt++)
            {
                var now = clock.GetUtcNow();
                var today = DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd");
                var document = await repository.GetAsync(userId, timeout.Token);
                var timestamps = (document?.RecentAdmissions ?? []).Where(x => x > now.AddSeconds(-60)).Order().ToList();
                var count = document?.UtcDay == today ? document.DailyCount : 0;
                var minuteReset = timestamps.Count == 0 ? now.AddSeconds(60) : timestamps[0].AddSeconds(60);
                var dayReset = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
                var minuteRemaining = Math.Max(0, PerMinuteLimit - timestamps.Count);
                var dayRemaining = Math.Max(0, PerDayLimit - count);

                if (minuteRemaining == 0 || dayRemaining == 0)
                {
                    var wait = minuteRemaining == 0 && dayRemaining == 0 ? (minuteReset > dayReset ? minuteReset : dayReset) : minuteRemaining == 0 ? minuteReset : dayReset;

                    return new(false, PerMinuteLimit, PerDayLimit, minuteRemaining, dayRemaining, Math.Max(1, (int)Math.Ceiling((wait - now).TotalSeconds)), minuteReset, dayReset);
                }
                timestamps.Add(now);
                var updated = new McpUsageDocument
                {
                    UserId = userId,
                    UtcDay = today,
                    DailyCount = count + 1,
                    RecentAdmissions = timestamps.TakeLast(PerMinuteLimit).ToList()
                };

                if (await repository.TrySaveAsync(updated, document?.ETag, timeout.Token))
                {
                    return new(true, PerMinuteLimit, PerDayLimit, minuteRemaining - 1, dayRemaining - 1, 0, minuteReset, dayReset);
                }
            }
            throw new UsageAdmissionUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UsageAdmissionUnavailableException();
        }
        catch (McpUsageStorageUnavailableException exception)
        {
            throw new UsageAdmissionUnavailableException(exception);
        }
    }
}

public sealed class UsageAdmissionUnavailableException(Exception? innerException = null)
    : Exception("Rate limit admission is temporarily unavailable.", innerException);
