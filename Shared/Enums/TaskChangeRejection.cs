namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<TaskChangeRejection>))]
public enum TaskChangeRejection
{
    None,
    InvalidInput,
    NotFound,
    Conflict,
    IdConflict,
    ProjectUnavailable,
    TimerActive,
    Capacity,
    StorageNotReady
}
