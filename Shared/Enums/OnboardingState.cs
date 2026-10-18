namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<OnboardingState>))]
public enum OnboardingState
{
    [JsonStringEnumMemberName("not-started")]
    NotStarted = 0,

    [JsonStringEnumMemberName("in-progress")]
    InProgress = 1,

    [JsonStringEnumMemberName("dismissed")]
    Dismissed = 2
}
