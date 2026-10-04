using Shared.Enums;
using Shared.Models;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceStatistics(WorkspaceStore store) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    private ReportPeriod _period = ReportPeriod.Week;
    private DateTime _lastDisplayedHour;

    // Invalidate date-based cards when the displayed hour changes.
    public void RefreshTime()
    {
        var now = UserNow;
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);

        if (hour == _lastDisplayedHour)
        {
            return;
        }

        _lastDisplayedHour = hour;
        NotifyChanged();
    }

    public ReportPeriod Period
    {
        get => _period;
        set
        {
            if (_period == value)
            {
                return;
            }

            _period = value;
            NotifyChanged();
        }
    }

    public string FirstName => Store.Data.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";

    public DateTime UserNow
    {
        get
        {
            try
            {
                return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(Store.Data.TimeZone)).DateTime;
            }
            catch { return DateTime.Now; }
        }
    }

    public DateOnly TodayDate => DateOnly.FromDateTime(UserNow);

    public string Greeting => UserNow.Hour < 12 ? "GOOD MORNING" : UserNow.Hour < 18 ? "GOOD AFTERNOON" : "GOOD EVENING";

    public IEnumerable<TaskRecord> PlannedTasks => Store.Data.Tasks.Where(t => t.PlannedToday);

    public IEnumerable<TaskRecord> DoneTasks => PlannedTasks.Where(t => t.IsComplete);

    public int TodayMinutes => Store.Data.Sessions.Where(s => LocalDate(s.StartedAt) == TodayDate).Sum(s => s.FocusMinutes);

    public int TodaySessions => Store.Data.Sessions.Count(s => LocalDate(s.StartedAt) == TodayDate);

    public int FocusStreak
    {
        get
        {
            var focusDays = Store.Data.Sessions.Select(session => LocalDate(session.StartedAt)).ToHashSet();
            var day = focusDays.Contains(TodayDate) ? TodayDate : TodayDate.AddDays(-1);
            var streak = 0;

            while (focusDays.Contains(day))
            {
                streak++;
                day = day.AddDays(-1);
            }

            return streak;
        }
    }

    public DateTime PeriodStart => Period switch
    {
        ReportPeriod.Day => UserNow.Date,
        ReportPeriod.Month => new(UserNow.Year, UserNow.Month, 1),
        _ => UserNow.Date.AddDays(-(((int)UserNow.DayOfWeek + 6) % 7))
    };

    public DateTime PeriodEnd => Period switch
    {
        ReportPeriod.Day => UserNow.Date.AddDays(1),
        ReportPeriod.Month => PeriodStart.AddMonths(1),
        _ => PeriodStart.AddDays(7)
    };

    public IEnumerable<FocusSessionRecord> PeriodRecords => Store.Data.Sessions.Where(session =>
    {
        var date = LocalDate(session.StartedAt).ToDateTime(TimeOnly.MinValue);

        return date >= PeriodStart && date < PeriodEnd;
    });

    public int PeriodMinutes => PeriodRecords.Sum(s => s.FocusMinutes);

    public int PeriodSessions => PeriodRecords.Count();

    public string PeriodLabel => Period switch
    {
        ReportPeriod.Day => "Today",
        ReportPeriod.Month => PeriodStart.ToString("MMMM yyyy"),
        _ => $"{PeriodStart:d MMM} to {PeriodEnd.AddDays(-1):d MMM}"
    };

    public string TopProjectName => Store.Data.Projects.OrderByDescending(p => PeriodProjectMinutes(p.Id)).FirstOrDefault()?.Name ?? "No projects yet";

    public int TopProjectMinutes => Store.Data.Projects.Select(p => PeriodProjectMinutes(p.Id)).DefaultIfEmpty().Max();

    public List<(string Label, int Minutes)> ChartDays
    {
        get
        {
            var start = PeriodStart;
            var end = PeriodEnd;

            return Enumerable.Range(0, Math.Clamp((int)(end - start).TotalDays, 1, 31)).Select(i =>
            {
                var date = start.AddDays(i);
                var minutes = PeriodRecords.Where(s => LocalDate(s.StartedAt) == DateOnly.FromDateTime(date)).Sum(s => s.FocusMinutes);
                var label = Period == ReportPeriod.Month ? date.ToString("d") : date.ToString("ddd")[..1];

                return (label, minutes);
            }).ToList();
        }
    }

    public int ProjectTodayMinutes(string id) => Store.Data.Sessions.Where(s => s.ProjectId == id && LocalDate(s.StartedAt) == TodayDate).Sum(s => s.FocusMinutes);

    public int TaskTodayMinutes(string id) => Store.Data.Sessions.Where(s => s.TaskId == id && LocalDate(s.StartedAt) == TodayDate).Sum(s => s.FocusMinutes);

    public int ProjectMinutes(string id) => Store.Data.Sessions.Where(s => s.ProjectId == id).Sum(s => s.FocusMinutes);

    public int PeriodProjectMinutes(string id) => PeriodRecords.Where(s => s.ProjectId == id).Sum(s => s.FocusMinutes);

    public DateOnly LocalDate(DateTimeOffset instant)
    {
        try
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(Store.Data.TimeZone)).DateTime);
        }
        catch { return DateOnly.FromDateTime(instant.LocalDateTime); }
    }
}
