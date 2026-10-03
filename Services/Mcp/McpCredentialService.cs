using System.Security.Cryptography;
using System.Text;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Services.Database;

namespace Services.Mcp;

public sealed record McpKeyMetadata(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Revoked);
public sealed record CreatedMcpKey(McpKeyMetadata Metadata, string Key);
public sealed record ValidatedMcpKey(string UserId, McpKeyDocument Document);

public interface IMcpCredentialService
{
    Task<CreatedMcpKey> CreateAsync(string userId, string name, int expiryDays, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<McpKeyMetadata>> ListAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<ValidatedMcpKey?> ValidateAsync(string? key, CancellationToken cancellationToken = default);
}

public sealed class McpCredentialService(CosmosClient cosmos, IConfiguration configuration, SecretClient? secrets = null) : IMcpCredentialService
{
    private Container Keys => cosmos.GetContainer(configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development", "McpKeys");

    public async Task<CreatedMcpKey> CreateAsync(string userId, string name, int expiryDays, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || expiryDays is not (30 or 90 or 365))
        {
            throw new ArgumentException("Provide a name up to 80 characters and an expiry of 30, 90, or 365 days.");
        }

        var id = Guid.NewGuid().ToString("N");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var fullKey = $"fm_{id}.{secret}";
        var vaultName = "mcp-" + id;
        var now = DateTimeOffset.UtcNow;
        var document = new McpKeyDocument { Id = id, UserId = userId, Name = name.Trim(), Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullKey))), VaultSecretName = vaultName, Prefix = fullKey[..Math.Min(13, fullKey.Length)], CreatedAt = now, ExpiresAt = now.AddDays(expiryDays) };

        if (secrets is null)
        {
            throw new InvalidOperationException("Key Vault is not configured.");
        }

        await secrets.SetSecretAsync(vaultName, fullKey, cancellationToken);

        try
        {
            await Keys.CreateItemAsync(document, new PartitionKey(userId), cancellationToken: cancellationToken);
        }
        catch
        {
            try
            {
                await Keys.DeleteItemAsync<McpKeyDocument>(id, new PartitionKey(userId), cancellationToken: CancellationToken.None);
            }
            catch { }

            try
            {
                await secrets.StartDeleteSecretAsync(vaultName, CancellationToken.None);
            }
            catch { }
            throw;
        }

        return new(new(id, document.Name, document.Prefix, now, document.ExpiresAt, false), fullKey);
    }

    public async Task<IReadOnlyList<McpKeyMetadata>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId ORDER BY c.createdAt DESC").WithParameter("@userId", userId);

        using var iterator = Keys.GetItemQueryIterator<McpKeyDocument>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });
        var result = new List<McpKeyMetadata>();

        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync(cancellationToken))
            {
                result.Add(new(item.Id, item.Name, item.Prefix, item.CreatedAt, item.ExpiresAt, item.RevokedAt is not null));
            }
        }

        return result;
    }

    public async Task<bool> RevokeAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Keys.ReadItemAsync<McpKeyDocument>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
            var doc = response.Resource;

            if (doc.RevokedAt is null)
            {
                doc.RevokedAt = DateTimeOffset.UtcNow;
                await Keys.ReplaceItemAsync(doc, id, new PartitionKey(userId), new ItemRequestOptions
                {
                    IfMatchEtag = response.ETag
                }, cancellationToken);
            }

            if (secrets is not null)
            {
                await secrets.StartDeleteSecretAsync(doc.VaultSecretName, cancellationToken);
            }

            return true;
        }
        catch (CosmosException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<ValidatedMcpKey?> ValidateAsync(string? key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("fm_", StringComparison.Ordinal))
        {
            return null;
        }

        var dot = key.IndexOf('.');

        if (dot < 4)
        {
            return null;
        }

        var id = key[3..dot];

        if (id.Length != 32 || !Guid.TryParseExact(id, "N", out _))
        {
            return null;
        }

        using var iterator = Keys.GetItemQueryIterator<McpKeyDocument>(new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id));
        McpKeyDocument? document = null;

        while (iterator.HasMoreResults && document is null)
        {
            document = (await iterator.ReadNextAsync(cancellationToken)).FirstOrDefault();
        }

        if (document is null || document.RevokedAt is not null || document.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        byte[] expected;

        try
        {
            expected = Convert.FromHexString(document.Digest);
        }
        catch (FormatException)
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(digest, expected) ? new(document.UserId, document) : null;
    }
}
