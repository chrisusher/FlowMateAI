using System.Net;
using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

public sealed class McpCredentialRepositoryTests
{
    [Test]
    public async Task CreateAsync_OriginalSdkDocumentWithoutId_CreatesQueryableEfDocument()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        using var client = testDatabase.CreateLegacySeedClient();
        var container = client.GetContainer(testDatabase.Name, "McpKeys");
        var document = new McpKeyDocument
        {
            Id = "sdk-key",
            UserId = "owner",
            Name = "SDK key",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };
        var failure = await Assert.ThrowsAsync<CosmosException>(() => container.CreateItemAsync(document, new PartitionKey("owner")));
        Assert.That(failure!.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        await using var context = testDatabase.CreateContext();
        var repository = new McpCredentialRepository(context);
        await repository.CreateAsync(document);
        Assert.That((await repository.GetAsync("owner", "sdk-key"))?.Name, Is.EqualTo("SDK key"));
    }

    [Test]
    public async Task Repository_LegacyDocumentAndConcurrentRevocation_PreservesIsolationAndRevision()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        using var seedClient = testDatabase.CreateLegacySeedClient();
        var container = seedClient.GetContainer(testDatabase.Name, "McpKeys");
        // Seed a compatible non-discriminated document with CLR property names.
        const string legacy = """
            {"id":"legacy", "UserId":"owner", "Name":"Assistant", "Digest":"ABCD", "VaultSecretName":"mcp-legacy", "Prefix":"fm_legacy", "CreatedAt":"2026-01-01T00:00:00+00:00", "ExpiresAt":"2030-01-01T00:00:00+00:00", "RevokedAt":null}
            """;
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(legacy));
        using var response = await container.CreateItemStreamAsync(content, new PartitionKey("owner"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        await using var firstContext = testDatabase.CreateContext();
        await using var secondContext = testDatabase.CreateContext();
        var first = new McpCredentialRepository(firstContext);
        var second = new McpCredentialRepository(secondContext);
        var original = await first.GetAsync("owner", "legacy");
        var stale = await second.GetAsync("owner", "legacy");
        Assert.That(original, Is.Not.Null);
        Assert.That(original!.Name, Is.EqualTo("Assistant"));
        Assert.That(original.ETag, Is.Not.Null.And.Not.Empty);
        Assert.That(await first.GetAsync("other", "legacy"), Is.Null);
        Assert.That(await first.ListAsync("other"), Is.Empty);
        Assert.That((await first.ListAsync("owner")).Select(item => item.Id), Is.EqualTo(new[] { "legacy" }));
        Assert.That((await first.FindByIdAsync("legacy"))!.UserId, Is.EqualTo("owner"));
        original.RevokedAt = DateTimeOffset.UtcNow;
        Assert.That(await first.ReplaceAsync(original, original.ETag), Is.True);
        stale!.Name = "stale";
        Assert.That(await second.ReplaceAsync(stale, stale.ETag), Is.False);
        Assert.That((await second.GetAsync("owner", "legacy"))!.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task CreateAsync_NewDocument_CanReadAndDeleteWithinOwnerPartition()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var context = testDatabase.CreateContext();
        var repository = new McpCredentialRepository(context);
        await repository.CreateAsync(new McpKeyDocument
        {
            Id = "new",
            UserId = "owner",
            Name = "Assistant",
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        Assert.That((await repository.GetAsync("owner", "new"))!.Name, Is.EqualTo("Assistant"));
        await repository.DeleteAsync("other", "new");
        Assert.That(await repository.GetAsync("owner", "new"), Is.Not.Null);
        await repository.DeleteAsync("owner", "new");
        Assert.That(await repository.GetAsync("owner", "new"), Is.Null);
    }
}
