namespace Shared.Enums;

/// <summary>Currencies offered for FlowMate subscriptions.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OfferCurrency>))]
public enum OfferCurrency
{
    Gbp,
    Usd,
    Eur
}
