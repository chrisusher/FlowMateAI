namespace Shared.Contracts;

public sealed record BillingActionResponse(bool Succeeded, string? Url, string? Code, string? Message);
