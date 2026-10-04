using Shared.Models;
using Shared.Enums;

namespace Shared.Exceptions;

public sealed class UserDataResetException(
    UserDataResetStage stage,
    UserDataResetResult completed,
    Exception innerException)
    : Exception($"User data reset failed during {GetStageLabel(stage)} cleanup.", innerException)
{
    public UserDataResetStage Stage { get; } = stage;
    public string StageLabel => GetStageLabel(Stage);
    
    public UserDataResetResult Completed { get; } = completed;

    private static string GetStageLabel(UserDataResetStage stage) => stage switch
    {
        UserDataResetStage.WorkspaceDocument => "workspace document",
        UserDataResetStage.WorkspaceRecord => "workspace record",
        UserDataResetStage.BillingEntitlement => "billing entitlement",
        UserDataResetStage.ArchivedBlob => "archived blob",
        UserDataResetStage.McpKeys => "MCP keys",
        UserDataResetStage.McpUsage => "MCP usage",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };
}
