namespace Shared.Enums;

public enum ReportPeriod
{
    [JsonStringEnumMemberName("day")]
    Day = 0,

    [JsonStringEnumMemberName("week")]
    Week = 1,

    [JsonStringEnumMemberName("month")]
    Month = 2
}


public static class ReportPeriodExtensions
{
    public static string ToWireValue(this ReportPeriod period) => period switch
    {
        ReportPeriod.Day => "day",
        ReportPeriod.Week => "week",
        ReportPeriod.Month => "month",
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, null)
    };

    public static bool TryParseWireValue(string value, out ReportPeriod period)
    {
        period = value.ToLowerInvariant() switch
        {
            "day" => ReportPeriod.Day,
            "week" => ReportPeriod.Week,
            "month" => ReportPeriod.Month,
            _ => (ReportPeriod)(-1)
        };

        return Enum.IsDefined(period);
    }
}
