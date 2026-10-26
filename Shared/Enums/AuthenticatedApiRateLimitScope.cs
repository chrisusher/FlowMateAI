namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<AuthenticatedApiRateLimitScope>))]
public enum AuthenticatedApiRateLimitScope
{
    WorkspaceWrite = 0,
    CoachRequest = 1,
    BillingRequest = 2
}
