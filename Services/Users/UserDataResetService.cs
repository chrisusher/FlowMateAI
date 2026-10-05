using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Services.Repositories;
using Services.Mcp;
using Shared.Exceptions;
using Shared.Enums;
using Shared.Models;

namespace Services.Users;

public interface IUserDataResetService
{
    Task<UserDataResetResult> ResetAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Removes application-owned data for one Keycloak subject. This deliberately never creates
/// the Cosmos database, containers, or the archive container.
/// </summary>
public sealed class UserDataResetService(IUserDataResetRepository repository, BlobServiceClient blobClient, IMcpCredentialService credentials) : IUserDataResetService
{
    private const string ActivityContainerName = "flowmate-activity";

    public async Task<UserDataResetResult> ResetAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var workspaceDocuments = 0;
        var workspaceRecords = 0;
        var billingEntitlements = 0;
        var archivedBlobs = 0;
        var mcpKeys = 0;
        var mcpUsageRecords = 0;

        async Task RunStageAsync(UserDataResetStage stage, Func<Task> action)
        {

            try
            {
                await action();
            }
            catch (Exception exception) when (exception is not UserDataResetException and not OperationCanceledException)
            {
                throw new UserDataResetException(stage,
                    new UserDataResetResult(workspaceDocuments, workspaceRecords, billingEntitlements, archivedBlobs)
                    {
                        McpKeys = mcpKeys,
                        McpUsageRecords = mcpUsageRecords
                    },
                    exception);
            }
        }

        await RunStageAsync(UserDataResetStage.WorkspaceDocument, async () =>
        {
            workspaceDocuments = await repository.DeleteWorkspaceDocumentsAsync(userId, cancellationToken);
        });

        await RunStageAsync(UserDataResetStage.WorkspaceRecord, async () =>
        {
            workspaceRecords = await repository.DeleteWorkspaceRecordsAsync(userId, cancellationToken);
        });

        await RunStageAsync(UserDataResetStage.BillingEntitlement, async () =>
        {
            billingEntitlements = await repository.DeleteBillingEntitlementsAsync(userId, cancellationToken);
        });

        await RunStageAsync(UserDataResetStage.ArchivedBlob, async () =>
        {
            var container = blobClient.GetBlobContainerClient(ActivityContainerName);

            if (!(await container.ExistsAsync(cancellationToken)).Value)
            {
                return;
            }

            var prefix = Uri.EscapeDataString(userId) + "/";

            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            {
                var response = await container.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken: cancellationToken);

                if (response.Value)
                {
                    archivedBlobs++;
                }
            }
        });

        await RunStageAsync(UserDataResetStage.McpKeys, async () => mcpKeys = await credentials.DeleteAllForUserAsync(userId, cancellationToken));
        
        await RunStageAsync(UserDataResetStage.McpUsage, async () => mcpUsageRecords = await repository.DeleteMcpUsageAsync(userId, cancellationToken));

        return new UserDataResetResult(workspaceDocuments, workspaceRecords, billingEntitlements, archivedBlobs)
        {
            McpKeys = mcpKeys,
            McpUsageRecords = mcpUsageRecords
        };
    }
}
