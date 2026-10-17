using System.Globalization;

namespace Shared.Data;

public class DateData
{
    public static string LocalDateString(DateTimeOffset instant, string timeZone)
    {
        try
        {
            var localDate = LocalDate(instant, timeZone);

            return localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return instant.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    public static DateOnly LocalDate(DateTimeOffset instant, string timeZone)
    {
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);
        }
        catch
        {
            return DateOnly.FromDateTime(instant.LocalDateTime);
        }
    }

    public static int MinutesForLocalDay(IEnumerable<Shared.Models.FocusSessionRecord> sessions, DateTimeOffset now, string timeZone)
    {
        var day = LocalDate(now, timeZone);

        return sessions.Where(s => LocalDate(s.StartedAt, timeZone) == day).Sum(s => s.FocusMinutes);
    }

    public static DateTime GetUserNow(string timeZone)
    {

        try
        {
            return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }
}
