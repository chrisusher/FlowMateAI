using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

public sealed class McpCredentialRepository(DatabaseContext database) : IMcpCredentialRepository
{
    public async Task CreateAsync(McpKeyDocument document, CancellationToken cancellationToken = default)
    {
        var entry = database.McpKeys.Add(document);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task DeleteAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        var document = await GetAsync(userId, id, cancellationToken);

        if (document is null)
        {
            return;
        }

        var entry = database.McpKeys.Remove(document);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    public async Task<IReadOnlyList<McpKeyDocument>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
        await database.McpKeys.AsNoTracking()
            .Where(document => document.UserId == userId)
            .OrderByDescending(document => document.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<McpKeyDocument?> GetAsync(string userId, string id, CancellationToken cancellationToken = default) =>
        database.McpKeys.AsNoTracking().FirstOrDefaultAsync(document => document.UserId == userId && document.Id == id, cancellationToken);

    public Task<McpKeyDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
        database.McpKeys.AsNoTracking().FirstOrDefaultAsync(document => document.Id == id, cancellationToken);

    public async Task<bool> ReplaceAsync(McpKeyDocument document, string? etag, CancellationToken cancellationToken = default)
    {
        var entry = database.McpKeys.Update(document);
        entry.Property(x => x.ETag).OriginalValue = etag;
        entry.Property(x => x.ETag).IsModified = false;

        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }
}
