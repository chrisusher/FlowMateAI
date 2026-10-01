namespace Shared.Contracts;

public sealed record BillingPrice(string Interval, string Currency, long UnitAmount, string Display);
