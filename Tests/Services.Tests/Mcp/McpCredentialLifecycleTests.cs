using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Database;
using Services.Mcp;
using Services.Repositories;

namespace Services.Tests.Mcp;

public sealed class McpCredentialLifecycleTests
{
    [Test]
    public async Task CreateAsync_ValidKey_ListsValidatesAndRevokesOnlyForOwner()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var context = testDatabase.CreateContext();
        var vault = new FakeSecretClient();
        var service = new McpCredentialService(new McpCredentialRepository(context), NullLogger<McpCredentialService>.Instance, vault);

        var created = await service.CreateAsync("owner", " Laptop ", 90);
        Assert.That(created.Metadata.Name, Is.EqualTo("Laptop"));
        Assert.That(vault.Values.Values, Does.Contain(created.Key));
        Assert.That((await service.ListAsync("owner")).Single().Id, Is.EqualTo(created.Metadata.Id));
        Assert.That(await service.ListAsync("other"), Is.Empty);
        Assert.That((await service.ValidateAsync(created.Key))?.UserId, Is.EqualTo("owner"));
        Assert.That(await service.ValidateAsync(created.Key + "x"), Is.Null);
        Assert.That(await service.RevokeAsync("other", created.Metadata.Id), Is.False);
        Assert.That(await service.ValidateAsync(created.Key), Is.Not.Null);
        Assert.That(await service.RevokeAsync("owner", created.Metadata.Id), Is.True);
        Assert.That(await service.ValidateAsync(created.Key), Is.Null);
        Assert.That((await service.ListAsync("owner")).Single().Revoked, Is.True);
        Assert.That(vault.Values, Is.Empty);
        Assert.That(await service.RevokeAsync("owner", created.Metadata.Id), Is.True);
    }

    [Test]
    public async Task CreateAsync_MetadataWriteFailure_DeletesVaultSecretAndMetadata()
    {
        var repository = new FailingRepository();
        var vault = new FakeSecretClient();
        var service = new McpCredentialService(repository, NullLogger<McpCredentialService>.Instance, vault);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("owner", "Laptop", 90));
        Assert.That(repository.Deleted, Is.True);
        Assert.That(vault.Values, Is.Empty);
        Assert.That(vault.DeleteCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_VaultWriteFailsAfterCommit_RemovesSecretWithoutSavingMetadata()
    {
        var repository = new FailingRepository();
        var vault = new FakeSecretClient
        {
            FailAfterSet = true
        };
        var service = new McpCredentialService(repository, NullLogger<McpCredentialService>.Instance, vault);

        await Assert.ThrowsAsync<RequestFailedException>(() => service.CreateAsync("owner", "Laptop", 90));
        Assert.That(repository.Deleted, Is.False);
        Assert.That(vault.Values, Is.Empty);
        Assert.That(vault.DeleteCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task RevokeAsync_VaultDeletionFails_RejectsKeyAndAllowsCleanupRetry()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var context = testDatabase.CreateContext();
        var vault = new FakeSecretClient();
        var service = new McpCredentialService(new McpCredentialRepository(context), NullLogger<McpCredentialService>.Instance, vault);
        var created = await service.CreateAsync("owner", "Laptop", 90);
        vault.FailNextDelete = true;

        await Assert.ThrowsAsync<RequestFailedException>(() => service.RevokeAsync("owner", created.Metadata.Id));
        Assert.That(await service.ValidateAsync(created.Key), Is.Null);
        Assert.That((await service.ListAsync("owner")).Single().VaultCleanupPending, Is.True);
        Assert.That(vault.Values, Has.Count.EqualTo(1));
        Assert.That(await service.RevokeAsync("owner", created.Metadata.Id), Is.True);
        Assert.That((await service.ListAsync("owner")).Single().VaultCleanupPending, Is.False);
        Assert.That(vault.Values, Is.Empty);
    }

    [Test]
    public async Task DeleteAllForUserAsync_MetadataDeletionFails_LeavesCredentialRevoked()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var context = testDatabase.CreateContext();
        var vault = new FakeSecretClient();
        var repository = new FailingDeleteRepository(new McpCredentialRepository(context));
        var service = new McpCredentialService(repository, NullLogger<McpCredentialService>.Instance, vault);
        var created = await service.CreateAsync("owner", "Laptop", 90);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAllForUserAsync("owner"));
        Assert.That(await service.ValidateAsync(created.Key), Is.Null);
        Assert.That((await service.ListAsync("owner")).Single().Revoked, Is.True);
        Assert.That(vault.Values, Is.Empty);
    }

    [Test]
    public async Task RevokeAsync_ConflictingRevision_DoesNotDeleteSecretUntilRevocationSucceeds()
    {
        var vault = new FakeSecretClient();
        vault.Values["mcp-test"] = "secret";
        var repository = new ConflictingRepository(vault);
        var service = new McpCredentialService(repository, NullLogger<McpCredentialService>.Instance, vault);

        Assert.That(await service.RevokeAsync("owner", "id"), Is.True);
        Assert.That(repository.Attempts, Is.EqualTo(3));
        Assert.That(repository.SecretWasPresentDuringRetry, Is.True);
        Assert.That(vault.Values, Is.Empty);
    }

    private sealed class FakeSecretClient : SecretClient
    {
        public Dictionary<string, string> Values { get; } = new();
        public int DeleteCalls { get; private set; }
        public bool FailAfterSet { get; init; }
        public bool FailNextDelete { get; set; }

        public override Task<Response<KeyVaultSecret>> SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
        {
            Values[name] = value;

            if (FailAfterSet)
            {
                throw new RequestFailedException(503, "The response was lost.");
            }

            return Task.FromResult<Response<KeyVaultSecret>>(null!);
        }

        public override Task<DeleteSecretOperation> StartDeleteSecretAsync(string name, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;

            if (FailNextDelete)
            {
                FailNextDelete = false;
                throw new RequestFailedException(403, "Deletion denied.");
            }

            if (!Values.Remove(name))
            {
                throw new RequestFailedException(404, "Not found");
            }

            return Task.FromResult<DeleteSecretOperation>(null!);
        }
    }

    private sealed class FailingRepository : IMcpCredentialRepository
    {
        public bool Deleted { get; private set; }
        public Task CreateAsync(McpKeyDocument document, CancellationToken cancellationToken = default) => throw new InvalidOperationException("storage unavailable");
        public Task DeleteAsync(string userId, string id, CancellationToken cancellationToken = default)
        {
            Deleted = true;

            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<McpKeyDocument>> ListAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<McpKeyDocument?> GetAsync(string userId, string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<McpKeyDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ReplaceAsync(McpKeyDocument document, string? etag, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ConflictingRepository(FakeSecretClient vault) : IMcpCredentialRepository
    {
        private DateTimeOffset? _revokedAt;
        private bool _cleanupPending;
        public int Attempts { get; private set; }
        public bool SecretWasPresentDuringRetry { get; private set; }
        public Task CreateAsync(McpKeyDocument document, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(string userId, string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<McpKeyDocument>> ListAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<McpKeyDocument?> GetAsync(string userId, string id, CancellationToken cancellationToken = default) => Task.FromResult<McpKeyDocument?>(new McpKeyDocument
        {
            Id = id,
            UserId = userId,
            ETag = Attempts.ToString(),
            VaultSecretName = "mcp-test",
            RevokedAt = _revokedAt,
            VaultCleanupPending = _cleanupPending
        });
        public Task<McpKeyDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ReplaceAsync(McpKeyDocument document, string? etag, CancellationToken cancellationToken = default)
        {
            Attempts++;

            if (Attempts == 2)
            {
                SecretWasPresentDuringRetry = vault.Values.ContainsKey("mcp-test");
            }

            if (Attempts == 1)
            {
                return Task.FromResult(false);
            }

            _revokedAt = document.RevokedAt;
            _cleanupPending = document.VaultCleanupPending;

            return Task.FromResult(true);
        }
    }

    private sealed class FailingDeleteRepository(IMcpCredentialRepository inner) : IMcpCredentialRepository
    {
        public Task CreateAsync(McpKeyDocument document, CancellationToken cancellationToken = default) => inner.CreateAsync(document, cancellationToken);
        public Task DeleteAsync(string userId, string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("storage unavailable");
        public Task<IReadOnlyList<McpKeyDocument>> ListAsync(string userId, CancellationToken cancellationToken = default) => inner.ListAsync(userId, cancellationToken);
        public Task<McpKeyDocument?> GetAsync(string userId, string id, CancellationToken cancellationToken = default) => inner.GetAsync(userId, id, cancellationToken);
        public Task<McpKeyDocument?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => inner.FindByIdAsync(id, cancellationToken);
        public Task<bool> ReplaceAsync(McpKeyDocument document, string? etag, CancellationToken cancellationToken = default) => inner.ReplaceAsync(document, etag, cancellationToken);
    }
}
