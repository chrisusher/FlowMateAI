using Shared.Enums;

namespace Shared.Contracts;

public sealed record WorkspaceMetadataResponse(
    string DisplayName,
    string TimeZone,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BillingPlan>))] BillingPlan Plan,
    string ClientId,
    bool Muted,
    [property: JsonConverter(typeof(JsonStringEnumConverter<OnboardingState>))] OnboardingState FirstRunState,
    string Revision,
    string ProjectsHash,
    string TasksHash,
    string SessionsHash,
    string ConversationsHash,
    string TimerHash)
{
    public static WorkspaceMetadataResponse Create(WorkspaceResponse response)
    {
        var workspace = response.Workspace;

        return new(
            workspace.DisplayName,
            workspace.TimeZone,
            workspace.Plan,
            workspace.ClientId,
            workspace.Muted,
            workspace.FirstRunState,
            response.Revision,
            WorkspaceCollectionManifest.Hash(workspace.Projects),
            WorkspaceCollectionManifest.Hash(workspace.Tasks),
            WorkspaceCollectionManifest.Hash(workspace.Sessions),
            WorkspaceCollectionManifest.Hash(workspace.Conversations),
            WorkspaceCollectionManifest.Hash(new[] { workspace.Timer }));
    }
}
