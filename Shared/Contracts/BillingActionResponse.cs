using Shared.Enums;

namespace Shared.Contracts;

public sealed record BillingActionResponse(
    bool Succeeded,
    string? Url,
    [property: JsonConverter(typeof(JsonStringEnumConverter<BillingActionCode>))]
    BillingActionCode? Code,
    string? Message);
