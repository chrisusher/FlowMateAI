using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Shared.Models;

namespace Services.Repositories;

public sealed class WorkspaceReportRepository(CosmosClient cosmos, IConfiguration configuration) : IWorkspaceReportRepository
{
    private Container WorkspaceContainer => cosmos.GetContainer(DatabaseName, "WorkspaceDocuments");
    private Container BillingContainer => cosmos.GetContainer(DatabaseName, "BillingEntitlements");
    private string DatabaseName => configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"] ?? "flowmate-Development";

    public async Task<string> GetRevisionAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await WorkspaceContainer.ReadItemAsync<WorkspaceEnvelope>("workspace", new PartitionKey(userId), cancellationToken: cancellationToken);

            return response.ETag ?? response.Resource.UpdatedAt.UtcTicks.ToString();
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return "missing";
        }
    }

    public async Task<WorkspaceReportSource> GetWorkspaceAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await WorkspaceContainer.ReadItemAsync<WorkspaceEnvelope>("workspace", new PartitionKey(userId), cancellationToken: cancellationToken);
        var workspace = JsonSerializer.Deserialize<WorkspaceSnapshot>(response.Resource.Payload, SharedCommon.JsonOptions) ?? new();

        return new(workspace, response.ETag ?? response.Resource.UpdatedAt.UtcTicks.ToString());
    }

    public async Task<BillingAccess?> GetBillingAccessAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await BillingContainer.ReadItemAsync<BillingEnvelope>("billing", new PartitionKey(userId), cancellationToken: cancellationToken);
            
            return new(response.Resource.Plan, response.Resource.SubscriptionStatus);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private sealed class WorkspaceEnvelope
    {
        public string Payload { get; set; } = "{}";
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class BillingEnvelope
    {
        public string Plan { get; set; } = "Free";
        public string SubscriptionStatus { get; set; } = "none";
    }
}
