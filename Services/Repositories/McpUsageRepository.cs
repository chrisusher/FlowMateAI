using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Services.Database;

namespace Services.Repositories;

public sealed class McpUsageRepository(DatabaseContext database) : IMcpUsageRepository
{
    public async Task<McpUsageDocument?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Retries must read the latest revision rather than reuse a tracked entity.
            return await database.McpUsage.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == "usage" && x.UserId == userId, cancellationToken);
        }
        catch (CosmosException exception)
        {
            throw new McpUsageStorageUnavailableException(exception);
        }
    }

    public async Task<bool> TrySaveAsync(McpUsageDocument document, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        var entry = database.Entry(document);
        document.ETag = expectedRevision;
        entry.State = expectedRevision is null ? EntityState.Added : EntityState.Modified;
        entry.Property(x => x.ETag).OriginalValue = expectedRevision;
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
        catch (DbUpdateException exception) when (exception.InnerException is CosmosException { StatusCode: HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed })
        {
            return false;
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            return false;
        }
        catch (DbUpdateException exception)
        {
            throw new McpUsageStorageUnavailableException(exception);
        }
        catch (CosmosException exception)
        {
            throw new McpUsageStorageUnavailableException(exception);
        }
        finally
        {
            // Failed inserts and stale updates must not remain pending on the next attempt.
            entry.State = EntityState.Detached;
        }
    }
}
