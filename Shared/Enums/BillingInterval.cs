namespace Shared.Enums;

[JsonConverter(typeof(JsonStringEnumConverter<BillingInterval>))]
public enum BillingInterval
{
    [JsonStringEnumMemberName("month")]
    Monthly = 0,

    [JsonStringEnumMemberName("year")]
    Annual = 1
}
