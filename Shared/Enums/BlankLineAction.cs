namespace Shared.Enums;

public enum BlankLineAction
{
#if NET10_0_OR_GREATER
    [JsonStringEnumMemberName("insert")]
#endif
    Insert = 0,

#if NET10_0_OR_GREATER
    [JsonStringEnumMemberName("remove")]
#endif
    Remove = 1
}
