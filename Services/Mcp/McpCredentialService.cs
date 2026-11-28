using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Services.Database;
using Services.Repositories;

namespace Services.Mcp;

public sealed record McpKeyMetadata(string Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool Revoked, bool VaultCleanupPending = false);
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

public sealed class McpCredentialService(IMcpCredentialRepository repository, ILogger<McpCredentialService> logger, SecretClient? secrets = null) : IMcpCredentialService
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
            logger.LogError("MCP credential vault operation {Operation} failed because Key Vault is not configured.", "set");
            throw new InvalidOperationException("Key Vault is not configured.");
        }

        try
        {
            await secrets.SetSecretAsync(vaultName, fullKey, cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            logger.LogError("MCP credential vault operation {Operation} failed with status {Status} and error code {ErrorCode}.", "set", exception.Status, exception.ErrorCode);

            if (exception.Status is 0 or 408 or >= 500)
            {
                await TryCleanUpVaultSecretAsync(vaultName);
            }

            throw;
        }
        catch (Exception exception)
        {
            logger.LogError("MCP credential vault operation {Operation} failed with exception type {ExceptionType}.", "set", exception.GetType().Name);
            await TryCleanUpVaultSecretAsync(vaultName);
            throw;
        }

        try
        {
            await repository.CreateAsync(document, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError("MCP credential metadata creation failed with exception type {ExceptionType}; starting cleanup for {SecretName}.", exception.GetType().Name, vaultName);

            try
            {
                await repository.DeleteAsync(userId, id, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogError("MCP credential metadata cleanup failed with exception type {ExceptionType}.", cleanupException.GetType().Name);
            }

            await TryCleanUpVaultSecretAsync(vaultName);

            throw;
        }

        return new(new(id, document.Name, document.Prefix, now, document.ExpiresAt, false), fullKey);
    }

    private async Task TryCleanUpVaultSecretAsync(string name)
    {
        try
        {
            await DeleteVaultSecretAsync(name, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError("MCP credential vault cleanup failed for {SecretName} with exception type {ExceptionType}.", name, exception.GetType().Name);
        }
    }

    public async Task<IReadOnlyList<McpKeyMetadata>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = new List<McpKeyMetadata>();

        foreach (var item in await repository.ListAsync(userId, cancellationToken))
        {
            result.Add(new(item.Id, item.Name, item.Prefix, item.CreatedAt, item.ExpiresAt, item.RevokedAt is not null, item.VaultCleanupPending));
        }

        return result;
    }

    public async Task<bool> RevokeAsync(string userId, string id, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var doc = await repository.GetAsync(userId, id, cancellationToken);

            if (doc is null)
            {
                return false;
            }

            if (doc.RevokedAt is not null)
            {
                await DeleteVaultSecretAsync(doc.VaultSecretName, cancellationToken);

                if (!doc.VaultCleanupPending)
                {
                    return true;
                }

                doc.VaultCleanupPending = false;

                if (await repository.ReplaceAsync(doc, doc.ETag, cancellationToken))
                {
                    return true;
                }

                continue;
            }

            doc.RevokedAt = DateTimeOffset.UtcNow;
            doc.VaultCleanupPending = true;

            if (await repository.ReplaceAsync(doc, doc.ETag, cancellationToken))
            {
                // Reload the new ETag before completing the vault cleanup.
                continue;
            }
        }

        throw new InvalidOperationException("The MCP credential changed repeatedly while it was being revoked.");
    }

    private async Task DeleteVaultSecretAsync(string name, CancellationToken cancellationToken)
    {
        if (secrets is null)
        {
            logger.LogError("MCP credential vault operation {Operation} failed because Key Vault is not configured.", "delete");
            throw new InvalidOperationException("Key Vault is not configured; MCP credential cleanup cannot be completed.");
        }

        try
        {
            await secrets.StartDeleteSecretAsync(name, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            // A previous revoke may already have deleted the secret.
        }
        catch (RequestFailedException exception)
        {
            logger.LogError("MCP credential vault operation {Operation} failed for {SecretName} with status {Status} and error code {ErrorCode}.", "delete", name, exception.Status, exception.ErrorCode);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError("MCP credential vault operation {Operation} failed for {SecretName} with exception type {ExceptionType}.", "delete", name, exception.GetType().Name);
            throw;
        }
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
            // Disable authentication before attempting either external cleanup step.
            await RevokeAsync(userId, document.Id, cancellationToken);
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
