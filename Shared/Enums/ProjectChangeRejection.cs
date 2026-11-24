namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<ProjectChangeRejection>))]
public enum ProjectChangeRejection
{
    None,
    NotFound,
    AlreadyArchived,
    AlreadyActive,
    ProjectLimit,
    TimerActive,
    Conflict,
    IdConflict,
    StorageNotReady,
    Capacity,
    InvalidInput
}
