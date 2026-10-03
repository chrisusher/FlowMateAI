namespace Shared.Enums;

public enum UserDataResetStage
{
    [JsonStringEnumMemberName("workspace-document")]
    WorkspaceDocument = 0,

    [JsonStringEnumMemberName("workspace-record")]
    WorkspaceRecord = 1,

    [JsonStringEnumMemberName("billing-entitlement")]
    BillingEntitlement = 2,
    
    [JsonStringEnumMemberName("archived-blob")]
    ArchivedBlob = 3,
    
    [JsonStringEnumMemberName("mcp-keys")]
    McpKeys = 4,
    
    [JsonStringEnumMemberName("mcp-usage")]
    McpUsage = 5
}
