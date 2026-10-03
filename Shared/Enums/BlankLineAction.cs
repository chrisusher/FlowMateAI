namespace Shared.Enums;

public enum BlankLineAction
{
    [JsonStringEnumMemberName("insert")]
    Insert = 0,

    [JsonStringEnumMemberName("remove")]
    Remove = 1
}
