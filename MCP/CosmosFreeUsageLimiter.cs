using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace MCP;

public sealed record FreeAdmission(bool Admitted, int PerMinuteLimit, int PerDayLimit, int MinuteRemaining, int DayRemaining, int RetryAfterSeconds, DateTimeOffset MinuteResetAt, DateTimeOffset DayResetAt);
public interface IFreeUsageLimiter
{
    int PerMinuteLimit { get; }
    int PerDayLimit { get; }
    Task<FreeAdmission> AdmitAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class CosmosFreeUsageLimiter(CosmosClient cosmos, IConfiguration configuration) : IFreeUsageLimiter
{
    private Container Usage => cosmos.GetContainer(configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development", "McpUsage");
    public int PerMinuteLimit { get; } = Math.Max(1, configuration.GetValue("Mcp:FreePerMinute", 10));
    public int PerDayLimit { get; } = Math.Max(1, configuration.GetValue("Mcp:FreePerDay", 100));

    public async Task<FreeAdmission> AdmitAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);

            for (var attempt = 0; attempt < 5 && DateTimeOffset.UtcNow < deadline; attempt++)
            {
                var now = DateTimeOffset.UtcNow;
                var today = DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd");
                UsageDocument? document = null;
                string? etag = null;

                try
                {
                    var read = await Usage.ReadItemAsync<UsageDocument>("usage", new PartitionKey(userId), cancellationToken: timeout.Token);
                    document = read.Resource; etag = read.ETag;
                }
                catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                }

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
                var updated = new UsageDocument
                {
                    Id = "usage",
                    UserId = userId,
                    UtcDay = today,
                    DailyCount = count + 1,
                    RecentAdmissions = timestamps.TakeLast(PerMinuteLimit).ToList(),
                    Ttl = 172800
                };

                try
                {
                    if (document is null)
                    {
                        await Usage.CreateItemAsync(updated, new PartitionKey(userId), cancellationToken: timeout.Token);
                    }
                    else
                    {
                        await Usage.ReplaceItemAsync(updated, "usage", new PartitionKey(userId), new ItemRequestOptions
                        {
                            IfMatchEtag = etag
                        }, timeout.Token);
                    }

                    return new(true, PerMinuteLimit, PerDayLimit, minuteRemaining - 1, dayRemaining - 1, 0, minuteReset, dayReset);
                }
                catch (CosmosException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
                {
                }
            }
            throw new UsageAdmissionUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UsageAdmissionUnavailableException();
        }
    }

    private sealed class UsageDocument { public string Id { get; set; } = "usage"; public string UserId { get; set; } = ""; public string UtcDay { get; set; } = ""; public int DailyCount { get; set; } public List<DateTimeOffset> RecentAdmissions { get; set; } = []; public int Ttl { get; set; } }
}
public sealed class UsageAdmissionUnavailableException : Exception;
