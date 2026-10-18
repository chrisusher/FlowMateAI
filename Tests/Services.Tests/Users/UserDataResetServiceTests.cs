using Azure;
using Azure.Storage.Blobs;
using Services.Mcp;
using Services.Repositories;
using Services.Users;
using Shared.Enums;
using Shared.Exceptions;

namespace Services.Tests.Users;

[TestFixture]
public sealed class UserDataResetServiceTests
{
    [Test]
    public async Task ResetDeletesAndReportsUserRecords()
    {
        var repository = new StubUserDataResetRepository();
        var service = CreateService(repository);

        var result = await service.ResetAsync("auth0|user-1");

        Assert.That(repository.UserRecordDeleteCalls, Is.EqualTo(1));
        Assert.That(result.UserRecords, Is.EqualTo(2));
    }

    [Test]
    public void FailureAfterUserRecordDeletionIncludesItsCountInTheCompletedReport()
    {
        var repository = new StubUserDataResetRepository { FailMcpUsageDelete = true };
        var service = CreateService(repository);

        var exception = Assert.ThrowsAsync<UserDataResetException>(() => service.ResetAsync("auth0|user-1"));

        Assert.That(exception!.Stage, Is.EqualTo(UserDataResetStage.McpUsage));
        Assert.That(exception.Completed.UserRecords, Is.EqualTo(2));
    }

    private static UserDataResetService CreateService(StubUserDataResetRepository repository) =>
        new(repository, new MissingBlobServiceClient(), new StubMcpCredentialService());

    private sealed class StubUserDataResetRepository : IUserDataResetRepository
    {
        public bool FailMcpUsageDelete { get; init; }
        public int UserRecordDeleteCalls { get; private set; }

        public Task<int> DeleteWorkspaceDocumentsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<int> DeleteWorkspaceRecordsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(3);
        public Task<int> DeleteBillingEntitlementsAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(1);

        public Task<int> DeleteUserRecordsAsync(string userId, CancellationToken cancellationToken = default)
        {
            UserRecordDeleteCalls++;

            return Task.FromResult(2);
        }

        public Task<int> DeleteMcpUsageAsync(string userId, CancellationToken cancellationToken = default) =>
            FailMcpUsageDelete
                ? Task.FromException<int>(new InvalidOperationException("Storage unavailable."))
                : Task.FromResult(4);
    }

    private sealed class StubMcpCredentialService : IMcpCredentialService
    {
        public Task<CreatedMcpKey> CreateAsync(string userId, string name, int expiryDays, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<McpKeyMetadata>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> RevokeAsync(string userId, string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> DeleteAllForUserAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult(5);

        public Task<ValidatedMcpKey?> ValidateAsync(string? key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MissingBlobServiceClient() : BlobServiceClient(new Uri("https://storage.example.test"), new BlobClientOptions())
    {
        public override BlobContainerClient GetBlobContainerClient(string blobContainerName) => new MissingBlobContainerClient();
    }

    private sealed class MissingBlobContainerClient() : BlobContainerClient(new Uri("https://storage.example.test/flowmate-activity"), new BlobClientOptions())
    {
        public override Task<Response<bool>> ExistsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Response.FromValue(false, null!));
    }
}
