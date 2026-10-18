namespace Shared.Enums;

#if NET10_0_OR_GREATER
[JsonConverter(typeof(JsonStringEnumConverter<BlankLineAction>))]
#endif
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
