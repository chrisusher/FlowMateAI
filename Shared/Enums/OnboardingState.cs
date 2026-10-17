namespace Shared.Enums;

public enum OnboardingState
{
    [JsonStringEnumMemberName("not-started")]
    NotStarted = 0,

    [JsonStringEnumMemberName("in-progress")]
    InProgress = 1,

    [JsonStringEnumMemberName("dismissed")]
    Dismissed = 2
}
