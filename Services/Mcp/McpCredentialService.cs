using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Services.Database;
using Services.Repositories;

namespace Services.Mcp;

public sealed record McpKeyMetadata(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Revoked);
public sealed record CreatedMcpKey(McpKeyMetadata Metadata, string Key);
public sealed record ValidatedMcpKey(string UserId, McpKeyDocument Document);

public interface IMcpCredentialService
{
    Task<CreatedMcpKey> CreateAsync(string userId, string name, int expiryDays, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<McpKeyMetadata>> ListAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(string userId, string id, CancellationToken cancellationToken = default);
    Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<ValidatedMcpKey?> ValidateAsync(string? key, CancellationToken cancellationToken = default);
}

public sealed class McpCredentialService(IMcpCredentialRepository repository, SecretClient? secrets = null) : IMcpCredentialService
{
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

        var document = new McpKeyDocument
        {
            Id = id,
            UserId = userId,
            Name = name.Trim(),
            Digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullKey))),
            VaultSecretName = vaultName,
            Prefix = fullKey[..Math.Min(13, fullKey.Length)],
            CreatedAt = now,
            ExpiresAt = now.AddDays(expiryDays)
        };

        if (secrets is null)
        {
            throw new InvalidOperationException("Key Vault is not configured.");
        }

        await secrets.SetSecretAsync(vaultName, fullKey, cancellationToken);

        try
        {
            await repository.CreateAsync(document, cancellationToken);
        }
        catch
        {
            try
            {
                await repository.DeleteAsync(userId, id, CancellationToken.None);
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
        var result = new List<McpKeyMetadata>();

        foreach (var item in await repository.ListAsync(userId, cancellationToken))
        {
            result.Add(new(item.Id, item.Name, item.Prefix, item.CreatedAt, item.ExpiresAt, item.RevokedAt is not null));
        }

        return result;
    }

    public async Task<bool> RevokeAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        var doc = await repository.GetAsync(userId, id, cancellationToken);

        if (doc is null)
        {
            return false;
        }

        if (doc.RevokedAt is null)
        {
            doc.RevokedAt = DateTimeOffset.UtcNow;
            await repository.ReplaceAsync(doc, doc.ETag, cancellationToken);
        }

        if (secrets is not null)
        {
            await secrets.StartDeleteSecretAsync(doc.VaultSecretName, cancellationToken);
        }

        return true;
    }

    public async Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var documents = await repository.ListAsync(userId, cancellationToken);

        if (documents.Count > 0 && secrets is null)
        {
            throw new InvalidOperationException("Key Vault is not configured; MCP credentials cannot be fully deleted.");
        }

        foreach (var document in documents)
        {
            try
            {
                await secrets!.StartDeleteSecretAsync(document.VaultSecretName, cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status == 404)
            {
                // A revoked key's secret may already be soft-deleted. Its absence is a completed cleanup step.
            }

            await repository.DeleteAsync(userId, document.Id, cancellationToken);
        }

        return documents.Count;
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

        var document = await repository.FindByIdAsync(id, cancellationToken);

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
