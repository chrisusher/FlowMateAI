using Shared.Data;
using Shared.Enums;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceStatistics(WorkspaceStore store) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    private ReportPeriod _period = ReportPeriod.Week;
    private DateOnly? _selectedDate;
    private bool _followsToday = true;
    private DateTime _lastDisplayedHour;

    // Invalidate date-based cards when the displayed hour changes.
    public void RefreshTime()
    {
        var now = DateData.GetUserNow(Store.Data.TimeZone);
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
            _selectedDate = TodayDate;
            _followsToday = true;
            NotifyChanged();
        }
    }

    public DateOnly SelectedDate
    {
        get => _followsToday || _selectedDate is null ? TodayDate : _selectedDate.Value;
        set
        {
            var selected = value > TodayDate ? TodayDate : value;

            var followsToday = selected == TodayDate;

            if (_selectedDate == selected && _followsToday == followsToday)
            {
                return;
            }

            _selectedDate = selected;
            _followsToday = followsToday;
            NotifyChanged();
        }
    }

    public string FirstName => Store.Data.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";

    public DateOnly TodayDate => DateOnly.FromDateTime(DateData.GetUserNow(Store.Data.TimeZone));

    public DateOnly AvailableHistoryStart => Store.Data.Plan == BillingPlan.Pro
        ? TodayDate.AddYears(-1).AddDays(1)
        : TodayDate.AddDays(-29);

    public DateOnly AvailableHistoryEnd => TodayDate;

    public string Greeting => DateData.GetUserNow(Store.Data.TimeZone).Hour < 12 ? "GOOD MORNING" : DateData.GetUserNow(Store.Data.TimeZone).Hour < 18 ? "GOOD AFTERNOON" : "GOOD EVENING";

    public IEnumerable<TaskRecord> PlannedTasks => Store.Data.Tasks.Where(t => t.PlannedToday && Store.Project(t.ProjectId) is
    {
        IsArchived: false
    });

    public IEnumerable<TaskRecord> DoneTasks => PlannedTasks.Where(t => t.IsComplete);

    public int TodayMinutes => Store.Data.Sessions
        .Where(s => DateData.LocalDate(s.StartedAt, Store.Data.TimeZone) == TodayDate)
        .Sum(s => s.FocusMinutes);

    public int TodaySessions => Store.Data.Sessions.Count(s => DateData.LocalDate(s.StartedAt, Store.Data.TimeZone) == TodayDate);

    public int FocusStreak
    {
        get
        {
            var focusDays = Store.Data.Sessions
                .Select(session => DateData.LocalDate(session.StartedAt, Store.Data.TimeZone))
                .ToHashSet();

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

    public DateTime UserNow => DateData.GetUserNow(Store.Data.TimeZone);

    public DateOnly PeriodStart => Period switch
    {
        ReportPeriod.Day => SelectedDate,
        ReportPeriod.Month => new(SelectedDate.Year, SelectedDate.Month, 1),
        _ => SelectedDate.AddDays(-(((int)SelectedDate.DayOfWeek + 6) % 7))
    };

    public DateOnly PeriodEnd => Period switch
    {
        ReportPeriod.Day => PeriodStart,
        ReportPeriod.Month => PeriodStart.AddMonths(1).AddDays(-1),
        _ => PeriodStart.AddDays(6)
    };

    public DateOnly EffectivePeriodStart => PeriodStart < AvailableHistoryStart ? AvailableHistoryStart : PeriodStart;

    public DateOnly EffectivePeriodEnd => PeriodEnd > AvailableHistoryEnd ? AvailableHistoryEnd : PeriodEnd;

    public bool HistoryIsTruncated => EffectivePeriodStart > PeriodStart;

    public bool CanViewPreviousPeriod => PeriodStart.AddDays(-1) >= AvailableHistoryStart;

    public bool CanViewNextPeriod => PeriodStart < PeriodStartFor(TodayDate);

    public void PreviousPeriod()
    {
        if (CanViewPreviousPeriod)
        {
            SelectedDate = AddPeriod(SelectedDate, -1);
        }
    }

    public void NextPeriod()
    {
        if (CanViewNextPeriod)
        {
            SelectedDate = AddPeriod(SelectedDate, 1);
        }
    }

    public IEnumerable<FocusSessionRecord> PeriodRecords => Store.Data.Sessions.Where(session =>
    {
        var date = DateData.LocalDate(session.StartedAt, Store.Data.TimeZone);

        return date >= EffectivePeriodStart && date <= EffectivePeriodEnd && session.FocusMinutes > 0 && session.EndedAt > session.StartedAt;
    });

    public int PeriodMinutes => PeriodRecords.Sum(s => s.FocusMinutes);

    public int PeriodSessions => PeriodRecords.Count();

    public string PeriodLabel => Period switch
    {
        ReportPeriod.Day when SelectedDate == TodayDate => "Today",
        ReportPeriod.Day => SelectedDate.ToDateTime(TimeOnly.MinValue).ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB")),
        ReportPeriod.Month => PeriodStart.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-GB")),
        _ => $"{PeriodStart.ToDateTime(TimeOnly.MinValue):d MMM} to {PeriodEnd.ToDateTime(TimeOnly.MinValue):d MMM yyyy}"
    };

    public string TopProjectName
    {
        get
        {
            if (Store.Data.Projects.Count == 0)
            {
                return "No projects yet";
            }

            var project = Store.Data.Projects
                .OrderByDescending(p => PeriodProjectMinutes(p.Id))
                .First();

            return PeriodProjectMinutes(project.Id) == 0
                ? "No focus yet"
                : project.IsArchived ? $"{project.Name} (archived)" : project.Name;
        }
    }

    public int TopProjectMinutes => Store.Data.Projects
        .Select(p => PeriodProjectMinutes(p.Id))
        .DefaultIfEmpty()
        .Max();

    public List<(string Label, int Minutes)> ChartDays
    {
        get
        {
            var start = EffectivePeriodStart.ToDateTime(TimeOnly.MinValue);
            var end = PeriodEnd.ToDateTime(TimeOnly.MinValue).AddDays(1);

            if (EffectivePeriodStart > EffectivePeriodEnd || end <= start)
            {
                return [];
            }

            return Enumerable
                .Range(0, Math.Clamp((int)(end - start).TotalDays, 1, 31))
                .Select(i =>
            {
                var date = start.AddDays(i);
                var minutes = PeriodRecords.Where(s => DateData.LocalDate(s.StartedAt, Store.Data.TimeZone) == DateOnly.FromDateTime(date)).Sum(s => s.FocusMinutes);
                var label = Period == ReportPeriod.Month ? date.ToString("d") : date.ToString("ddd")[..1];

                return (label, minutes);
            }).ToList();
        }
    }

    public int ProjectTodayMinutes(string id) => Store.Data.Sessions
        .Where(s => s.ProjectId == id && DateData.LocalDate(s.StartedAt, Store.Data.TimeZone) == TodayDate)
        .Sum(s => s.FocusMinutes);

    public int TaskTodayMinutes(string id) => Store.Data.Sessions
        .Where(s => s.TaskId == id && DateData.LocalDate(s.StartedAt, Store.Data.TimeZone) == TodayDate)
        .Sum(s => s.FocusMinutes);

    public int ProjectMinutes(string id) => Store.Data.Sessions
        .Where(s => s.ProjectId == id)
        .Sum(s => s.FocusMinutes);

    public int PeriodProjectMinutes(string id) => PeriodRecords
        .Where(s => s.ProjectId == id)
        .Sum(s => s.FocusMinutes);

    private DateOnly AddPeriod(DateOnly date, int amount) => Period switch
    {
        ReportPeriod.Day => date.AddDays(amount),
        ReportPeriod.Week => date.AddDays(amount * 7),
        ReportPeriod.Month => date.AddMonths(amount),
        _ => throw new ArgumentOutOfRangeException(nameof(Period), Period, null)
    };

    private DateOnly PeriodStartFor(DateOnly date) => Period switch
    {
        ReportPeriod.Day => date,
        ReportPeriod.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        ReportPeriod.Month => new(date.Year, date.Month, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(Period), Period, null)
    };
}
