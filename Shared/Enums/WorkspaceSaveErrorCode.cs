namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceSaveErrorCode>))]
public enum WorkspaceSaveErrorCode
{
    [JsonStringEnumMemberName("revision-conflict")]
    RevisionConflict = 0,

    [JsonStringEnumMemberName("project-limit")]
    ProjectLimit = 1,

    [JsonStringEnumMemberName("timer-conflict")]
    TimerConflict = 2
}
