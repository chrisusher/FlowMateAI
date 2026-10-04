namespace Shared.Enums;

public enum WorkspaceSaveErrorCode
{
    [JsonStringEnumMemberName("revision-conflict")]
    RevisionConflict = 0,

    [JsonStringEnumMemberName("project-limit")]
    ProjectLimit = 1,

    [JsonStringEnumMemberName("timer-conflict")]
    TimerConflict = 2
}
