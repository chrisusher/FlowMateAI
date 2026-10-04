namespace Shared.Enums;

public enum WorkspaceSyncState
{
    [JsonStringEnumMemberName("saved")]
    Saved = 0,

    [JsonStringEnumMemberName("saving")]
    Saving = 1,

    [JsonStringEnumMemberName("offline")]
    Offline = 2,
    
    [JsonStringEnumMemberName("conflict")]
    Conflict = 3,
    
    [JsonStringEnumMemberName("rejected")]
    Rejected = 4,
    
    [JsonStringEnumMemberName("failed")]
    Failed = 5
}
