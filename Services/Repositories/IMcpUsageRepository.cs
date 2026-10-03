using Services.Database;

namespace Services.Repositories;

public interface IMcpUsageRepository
{
    Task<McpUsageDocument?> GetAsync(string userId, CancellationToken cancellationToken = default);

    // A null revision creates a document; an existing revision requires a conditional update.
    // False means a competing admission won the write and the caller must read again.
    Task<bool> TrySaveAsync(McpUsageDocument document, string? expectedRevision, CancellationToken cancellationToken = default);
}

public sealed class McpUsageStorageUnavailableException(Exception innerException)
    : Exception("Rate limit storage is temporarily unavailable.", innerException);
