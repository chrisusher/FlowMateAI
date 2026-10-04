using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Services.Database;

namespace Services.Repositories;

public sealed class McpCredentialRepository(CosmosClient cosmos, IConfiguration configuration) : IMcpCredentialRepository
{
    private Container Keys => cosmos.GetContainer(
        configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development",
        "McpKeys");

    public async Task CreateAsync(McpKeyDocument document, CancellationToken cancellationToken = default) =>
        await Keys.CreateItemAsync(document, new PartitionKey(document.UserId), cancellationToken: cancellationToken);

    public async Task DeleteAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        try
        {
            await Keys.DeleteItemAsync<McpKeyDocument>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
    }

    public async Task<IReadOnlyList<McpKeyDocument>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId ORDER BY c.createdAt DESC").WithParameter("@userId", userId);
        using var iterator = Keys.GetItemQueryIterator<McpKeyDocument>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });
        var result = new List<McpKeyDocument>();

        while (iterator.HasMoreResults)
        {
            result.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return result;
    }

    public async Task<McpKeyDocument?> GetAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Keys.ReadItemAsync<McpKeyDocument>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
            response.Resource.ETag = response.ETag;

            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<McpKeyDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        using var iterator = Keys.GetItemQueryIterator<McpKeyDocument>(new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id));

        while (iterator.HasMoreResults)
        {
            var item = (await iterator.ReadNextAsync(cancellationToken)).FirstOrDefault();

            if (item is not null)
            {
                return item;
            }
        }

        return null;
    }

    public async Task<bool> ReplaceAsync(McpKeyDocument document, string? etag, CancellationToken cancellationToken = default)
    {
        try
        {
            await Keys.ReplaceItemAsync(document, document.Id, new PartitionKey(document.UserId), new ItemRequestOptions
            {
                IfMatchEtag = etag
            }, cancellationToken);

            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
