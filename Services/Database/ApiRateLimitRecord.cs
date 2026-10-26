using Shared.Enums;

namespace Services.Database;

public sealed class ApiRateLimitRecord
{
    public AuthenticatedApiRateLimitScope Scope { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
