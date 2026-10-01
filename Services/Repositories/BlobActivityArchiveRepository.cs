using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure;
using Microsoft.Extensions.Azure;
using Shared.Models;

namespace Services.Repositories;

public sealed class BlobActivityArchiveRepository(IAzureClientFactory<BlobServiceClient> clients) : IActivityArchiveRepository
{
    private const string ContainerName = "flowmate-activity";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ArchiveAsync(string ownerId, IReadOnlyCollection<FocusSessionRecord> sessions, CancellationToken cancellationToken = default)
    {
        if (sessions.Count == 0)
            return;
        var container = clients.CreateClient("FlowMate").GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        foreach (var session in sessions)
        {
            var blobName = $"{Uri.EscapeDataString(ownerId)}/{session.StartedAt:yyyy/MM}/{session.Id}.json";
            var blob = container.GetBlobClient(blobName);
            await using var content = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(session, JsonOptions));
            try
            { await blob.UploadAsync(content, overwrite: false, cancellationToken); }
            catch (RequestFailedException exception) when (exception.Status == 409) { }
        }
    }
}
