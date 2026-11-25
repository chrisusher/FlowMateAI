namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<TaskGuardOperationKind>))]
public enum TaskGuardOperationKind
{
    Create,
    Move,
    Delete
}
