namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<ProjectNameValidation>))]
public enum ProjectNameValidation
{
    Valid,
    Required,
    TooLong,
    ContainsControlCharacters
}
