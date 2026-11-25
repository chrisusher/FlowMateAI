using System.Globalization;
using System.Text.Json;
using Shared.Contracts;
using Shared.Enums;

namespace Web.Components.Features.Reports;

public partial class FocusTimelineCard
{
    private static readonly int[] Hours = [0, 4, 8, 12, 16, 20, 24];
    private FocusTimeline? _timeline;
    private DateOnly? _selectedWeek;
    private bool _loading = true;
    private bool _error;
    private bool _disposed;
    private int _requestNumber;
    private string? _workspaceFingerprint;

    private bool CanGoPrevious => !_loading && _timeline is not null && _timeline.WeekStart > _timeline.HistoryAvailableFrom;
    private bool CanGoNext => !_loading && _timeline is not null && _timeline.WeekEnd < _timeline.HistoryAvailableThrough;
    private string WeekLabel => _timeline is null ? "Current week" : $"{FormatDate(_timeline.WeekStart)} – {FormatDate(_timeline.WeekEnd)}";

    protected override async Task OnInitializedAsync()
    {
        Store.Changed += WorkspaceChanged;
        _workspaceFingerprint = Fingerprint();
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var request = ++_requestNumber;
        _loading = true;
        _error = false;

        try
        {
            var result = await Items.GetFocusTimelineAsync(_selectedWeek);

            if (request == _requestNumber && !_disposed)
            {
                _timeline = result;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException or JsonException)
        {
            if (request == _requestNumber && !_disposed)
            {
                _error = true;
            }
        }
        finally
        {
            if (request == _requestNumber && !_disposed)
            {
                _loading = false;
            }
        }
    }

    private async Task PreviousWeek()
    {
        if (CanGoPrevious)
        {
            _selectedWeek = _timeline!.WeekStart.AddDays(-7);
            await LoadAsync();
        }
    }

    private async Task NextWeek()
    {
        if (CanGoNext)
        {
            _selectedWeek = _timeline!.WeekStart.AddDays(7);
            await LoadAsync();
        }
    }

    private void WorkspaceChanged()
    {
        if (_disposed || Store.SyncState != WorkspaceSyncState.Saved)
        {
            return;
        }
        var fingerprint = Fingerprint();

        if (fingerprint == _workspaceFingerprint)
        {
            return;
        }
        _workspaceFingerprint = fingerprint;
        _ = InvokeAsync(async () =>
        {
            await LoadAsync();

            if (!_disposed)
            {
                StateHasChanged();
            }
        });
    }

    private string Fingerprint() => JsonSerializer.Serialize(new { Store.Data.TimeZone, Store.Data.Plan, Store.Data.Sessions, Store.Data.Projects, Store.Data.Tasks });

    private static string FormatDate(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    private string Describe(FocusTimelineEntry entry)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_timeline!.TimeZone);
        var start = TimeZoneInfo.ConvertTime(entry.StartedAt, zone);
        var end = TimeZoneInfo.ConvertTime(entry.EndedAt, zone);
        var accuracy = entry.IsApproximate ? ", approximate" : "";

        return $"{entry.ProjectName}, {entry.TaskName}, {start:HH:mm zzz} to {end:HH:mm zzz}, {entry.FocusSeconds / 60:0.#} minutes focused{accuracy}";
    }

    private string BlockStyle(FocusTimelineEntry entry)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(_timeline!.TimeZone);
        var start = TimeZoneInfo.ConvertTime(entry.StartedAt, zone);
        var end = TimeZoneInfo.ConvertTime(entry.EndedAt, zone);
        var startMinutes = start.TimeOfDay.TotalMinutes;
        var endMinutes = start.Offset != end.Offset
            ? startMinutes + (entry.EndedAt - entry.StartedAt).TotalMinutes
            : DateOnly.FromDateTime(end.DateTime) > entry.LocalDate ? 1440 : end.TimeOfDay.TotalMinutes;

        if (endMinutes <= startMinutes)
        {
            endMinutes = Math.Min(1440, startMinutes + (entry.EndedAt - entry.StartedAt).TotalMinutes);
        }
        var left = startMinutes / 1440 * 100;
        var width = Math.Max(0.2, (endMinutes - startMinutes) / 1440 * 100);

        return FormattableString.Invariant($"left:{left:0.###}%;width:{width:0.###}%;background-color:{entry.ProjectColour}");
    }

    public void Dispose()
    {
        _disposed = true;
        Store.Changed -= WorkspaceChanged;
        GC.SuppressFinalize(this);
    }
}
