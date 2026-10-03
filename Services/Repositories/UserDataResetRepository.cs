using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Services.Database;

namespace Services.Repositories;

public sealed class UserDataResetRepository(DatabaseContext database, IConfiguration configuration, CosmosClient? cosmos = null) : IUserDataResetRepository
{
    private const string UsageContainerName = "McpUsage";

    public async Task<int> DeleteWorkspaceDocumentsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var matches = await ReadOrEmptyAsync(() => database.Workspaces.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        database.Workspaces.RemoveRange(matches);
        await database.SaveChangesAsync(cancellationToken);

        return matches.Count;
    }

    public async Task<int> DeleteWorkspaceRecordsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var matches = await ReadOrEmptyAsync(() => database.WorkspaceRecords.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        database.WorkspaceRecords.RemoveRange(matches);
        await database.SaveChangesAsync(cancellationToken);
        return matches.Count;
    }

    public async Task<int> DeleteBillingEntitlementsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var matches = await ReadOrEmptyAsync(() => database.BillingEntitlements.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        database.BillingEntitlements.RemoveRange(matches);
        await database.SaveChangesAsync(cancellationToken);

        return matches.Count;
    }

    public async Task<int> RevokeMcpKeysAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (cosmos is null)
        {
            return 0;
        }

        var container = cosmos.GetContainer(DatabaseName, "McpKeys");
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId").WithParameter("@userId", userId);

        using var iterator = container.GetItemQueryIterator<McpKeyDocument>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });
        var count = 0;

        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync(cancellationToken))
            {
                item.RevokedAt = DateTimeOffset.UtcNow;
                await container.ReplaceItemAsync(item, item.Id, new PartitionKey(userId), cancellationToken: cancellationToken);
                count++;
            }
        }

        return count;
    }

    public async Task<int> DeleteMcpUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (cosmos is null)
        {
            return 0;
        }

        try
        {
            await cosmos.GetContainer(DatabaseName, UsageContainerName).DeleteItemAsync<object>("usage", new PartitionKey(userId), cancellationToken: cancellationToken);
            return 1;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return 0;
        }
    }

    private string DatabaseName => configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development";

    private static async Task<List<T>> ReadOrEmptyAsync<T>(Func<Task<List<T>>> read)
    {
        try
        {
            return await read();
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }
    }
}
