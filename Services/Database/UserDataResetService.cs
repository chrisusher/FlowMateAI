using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;

namespace Services.Database;

public sealed record UserDataResetResult(int WorkspaceDocuments, int WorkspaceRecords, int BillingEntitlements, int ArchivedBlobs)
{
    public static UserDataResetResult Empty { get; } = new(0, 0, 0, 0);
}

public sealed class UserDataResetException(
    string stage,
    UserDataResetResult completed,
    Exception innerException)
    : Exception($"User data reset failed during {stage} cleanup.", innerException)
{
    public string Stage { get; } = stage;
    public UserDataResetResult Completed { get; } = completed;
}

public interface IUserDataResetService
{
    Task<UserDataResetResult> ResetAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Removes application-owned data for one Auth0 subject. This deliberately never creates
/// the Cosmos database, containers, or the archive container.
/// </summary>
public sealed class UserDataResetService(DatabaseContext database, BlobServiceClient blobClient) : IUserDataResetService
{
    private const string ActivityContainerName = "flowmate-activity";

    public async Task<UserDataResetResult> ResetAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var workspaceDocuments = 0;
        var workspaceRecords = 0;
        var billingEntitlements = 0;
        var archivedBlobs = 0;

        async Task RunStageAsync(string stage, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception) when (exception is not UserDataResetException and not OperationCanceledException)
            {
                throw new UserDataResetException(stage,
                    new UserDataResetResult(workspaceDocuments, workspaceRecords, billingEntitlements, archivedBlobs),
                    exception);
            }
        }

        await RunStageAsync("workspace document", async () =>
        {
            List<WorkspaceDocument> matches;
            try
            { matches = await database.Workspaces.Where(item => item.UserId == userId).ToListAsync(cancellationToken); }
            catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { return; }
            database.Workspaces.RemoveRange(matches);
            await database.SaveChangesAsync(cancellationToken);
            workspaceDocuments = matches.Count;
        });

        await RunStageAsync("workspace record", async () =>
        {
            List<WorkspaceRecordDocument> matches;
            try
            { matches = await database.WorkspaceRecords.Where(item => item.UserId == userId).ToListAsync(cancellationToken); }
            catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { return; }
            database.WorkspaceRecords.RemoveRange(matches);
            await database.SaveChangesAsync(cancellationToken);
            workspaceRecords = matches.Count;
        });

        await RunStageAsync("billing entitlement", async () =>
        {
            List<BillingEntitlementDocument> matches;
            try
            { matches = await database.BillingEntitlements.Where(item => item.UserId == userId).ToListAsync(cancellationToken); }
            catch (CosmosException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound) { return; }
            database.BillingEntitlements.RemoveRange(matches);
            await database.SaveChangesAsync(cancellationToken);
            billingEntitlements = matches.Count;
        });

        await RunStageAsync("archived blob", async () =>
        {
            var container = blobClient.GetBlobContainerClient(ActivityContainerName);

            if (!(await container.ExistsAsync(cancellationToken)).Value)
                return;

            var prefix = Uri.EscapeDataString(userId) + "/";
            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            {
                var response = await container.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);

                if (response.Value)
                    archivedBlobs++;
            }
        });

        return new UserDataResetResult(workspaceDocuments, workspaceRecords, billingEntitlements, archivedBlobs);
    }
}
