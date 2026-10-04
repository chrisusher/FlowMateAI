using Services.Database;

namespace Services.Repositories;

public interface IMcpCredentialRepository
{
    Task CreateAsync(McpKeyDocument document, CancellationToken cancellationToken = default);
    Task DeleteAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<McpKeyDocument>> ListAsync(string userId, CancellationToken cancellationToken = default);
    Task<McpKeyDocument?> GetAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<McpKeyDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> ReplaceAsync(McpKeyDocument document, string? etag, CancellationToken cancellationToken = default);
}
