using Microsoft.JSInterop;
using Shared.Enums;
using Shared.Models;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceTimerManager(WorkspaceStore store, WorkspaceStatistics statistics, IJSRuntime js) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    private IJSObjectReference? _workspaceModule;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _clockCancellation;
    private Task? _clockTask;
    public string? CompletionMessage { get; private set; }

    public void StartClock()
    {
        if (_clockTask is not null)
        {
            return;
        }

        _clockCancellation = new();
        _clockTask = RunClockAsync(_clockCancellation.Token);
    }

    public async Task StopClockAsync()
    {
        if (_clockCancellation is not null)
        {
            await _clockCancellation.CancelAsync();

            if (_clockTask is not null)
            {
                await _clockTask;
            }

            _clockCancellation.Dispose();
            _clockCancellation = null;
            _clockTask = null;
        }

        if (_workspaceModule is not null)
        {
            await _workspaceModule.DisposeAsync();
            _workspaceModule = null;
        }
    }

    private async Task RunClockAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await _gate.WaitAsync(cancellationToken);

                try
                {
                    if (IsRunning && CanControlTimer && SecondsLeft <= 0)
                    {
                        await TimerElapsed();
                    }

                    if (IsRunning)
                    {
                        NotifyChanged();
                        statistics.RefreshTime();
                    }
                }
                finally { _gate.Release(); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ExecuteAsync(Func<Task> action, bool requiresOwnership = true)
    {
        await _gate.WaitAsync();

        try
        {
            if (!requiresOwnership || CanControlTimer)
            {
                await action();
            }
        }
        finally { _gate.Release(); }
    }

    public TaskRecord? ActiveTask => Store.Task(Store.Data.Timer.TaskId);

    public ProjectRecord? ActiveProject => Store.Project(Store.Data.Timer.ProjectId);

    public bool IsRunning => Store.Data.Timer.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak;

    public bool CanControlTimer => string.IsNullOrEmpty(Store.Data.Timer.OwnerClientId) || Store.Data.Timer.OwnerClientId == Store.ClientId;

    public string TimerLabel => Store.Data.Timer.Phase switch
    {
        TimerPhase.Focus => "FOCUS SESSION",
        TimerPhase.ShortBreak or TimerPhase.LongBreak => "TAKE A BREATHER",
        TimerPhase.Paused => "PAUSED",
        _ => "YOUR FOCUS SPACE"
    };

    public string TimerCaption => Store.Data.Timer.Phase switch
    {
        TimerPhase.ShortBreak or TimerPhase.LongBreak => "A moment for yourself",
        TimerPhase.Paused => "Pick up when you're ready",
        _ => "One thing at a time"
    };

    public int SecondsLeft => IsRunning && Store.Data.Timer.EndsAt is { } end ? Math.Max(0, (int)Math.Ceiling((end - Store.UtcNow).TotalSeconds)) : Store.Data.Timer.RemainingSeconds;

    public string TimeLeft => $"{SecondsLeft / 60:00}:{SecondsLeft % 60:00}";

    public double RingCircumference => 2 * Math.PI * 106;

    public double RingOffset => RingCircumference * (1 - (Store.Data.Timer.DurationSeconds <= 0 ? 1 : (double)SecondsLeft / Store.Data.Timer.DurationSeconds));

    private async Task TimerElapsed()
    {
        var timer = Store.Data.Timer;

        if (timer.Phase == TimerPhase.Focus)
        {
            var longBreak = (timer.CompletedPomodoros + 1) % 4 == 0;
            RecordTimerIntervals(timer.EndsAt ?? Store.UtcNow);
            timer.CompletedPomodoros++;
            CompletionMessage = longBreak
                ? "Focus complete. Your 15-minute long break has started."
                : "Focus complete. Your 5-minute short break has started.";
            BeginBreak(longBreak ? 15 : 5);
            await Play(TimerTone.Break);
        }
        else
        {
            StartFocusInternal();
            CompletionMessage = "Break complete. A new focus session has started.";
            await Play(TimerTone.Focus);
        }
        await Store.SaveAsync();
    }

    private void StartFocusInternal()
    {
        var timer = Store.Data.Timer;
        CompletionMessage = null;
        timer.CompletedIntervals.Clear();
        timer.OwnerClientId = Store.ClientId;
        timer.FocusSessionId = Guid.NewGuid().ToString("N");
        timer.Phase = TimerPhase.Focus;
        timer.DurationSeconds = 25 * 60;
        timer.RemainingSeconds = timer.DurationSeconds;
        timer.StartedAt = Store.UtcNow;
        timer.EndsAt = timer.StartedAt.Value.AddSeconds(timer.DurationSeconds);

        if (string.IsNullOrEmpty(timer.TaskId))
        {
            var task = statistics.PlannedTasks.FirstOrDefault(t => !t.IsComplete);

            if (task is not null)
            {
                timer.TaskId = task.Id;
                timer.ProjectId = task.ProjectId;
            }
        }
    }

    private async Task StartFocusCore()
    {
        if (Store.Data.Timer.Phase is TimerPhase.Focus or TimerPhase.Paused or TimerPhase.ShortBreak or TimerPhase.LongBreak)
        {
            return;
        }

        StartFocusInternal();
        await Store.SaveAsync();
    }

    private void BeginBreak(int minutes)
    {
        var t = Store.Data.Timer;
        t.Phase = minutes == 15 ? TimerPhase.LongBreak : TimerPhase.ShortBreak;
        t.DurationSeconds = minutes * 60;
        t.RemainingSeconds = t.DurationSeconds;
        t.StartedAt = Store.UtcNow;
        t.EndsAt = t.StartedAt.Value.AddSeconds(t.DurationSeconds);
    }

    private async Task PauseTimerCore()
    {
        var t = Store.Data.Timer;

        if (t.Phase != TimerPhase.Focus)
        {
            return;
        }

        if (SecondsLeft <= 0)
        {
            await TimerElapsed();

            return;
        }

        var now = Store.UtcNow;

        if (t.StartedAt is { } start && now > start)
        {
            t.CompletedIntervals.Add(new()
            {
                StartedAt = start,
                EndedAt = now
            });
        }

        t.RemainingSeconds = SecondsLeft;
        t.Phase = TimerPhase.Paused;
        t.EndsAt = null;
        t.StartedAt = null;
        t.PausedAt = now;
        await Store.SaveAsync();
    }

    private async Task ResumeTimerCore()
    {
        var t = Store.Data.Timer;

        if (t.Phase != TimerPhase.Paused)
        {
            return;
        }

        t.Phase = TimerPhase.Focus;
        t.StartedAt = Store.UtcNow;
        t.EndsAt = t.StartedAt.Value.AddSeconds(t.RemainingSeconds);
        t.PausedAt = null;
        await Store.SaveAsync();
    }

    private async Task EndFocusCore()
    {
        var t = Store.Data.Timer;

        if (t.Phase is not (TimerPhase.Focus or TimerPhase.Paused))
        {
            return;
        }

        RecordTimerIntervals(Store.UtcNow);
        CompletionMessage = null;
        ResetTimer();
        await Store.SaveAsync();
    }

    private async Task SkipBreakCore()
    {
        if (Store.Data.Timer.Phase is not (TimerPhase.ShortBreak or TimerPhase.LongBreak))
        {
            return;
        }

        ResetTimer();
        StartFocusInternal();
        CompletionMessage = "Break skipped. Your next focus session has started.";
        await Store.SaveAsync();
    }

    private void RecordTimerIntervals(DateTimeOffset activeEnd)
    {
        var t = Store.Data.Timer;
        var intervals = t.CompletedIntervals.ToList();

        if (t.StartedAt is { } start && activeEnd > start)
        {
            intervals.Add(new() { StartedAt = start, EndedAt = activeEnd });
        }

        Store.RecordFocusIntervals(intervals, t.TaskId, t.ProjectId, t.DurationSeconds, t.EnsureFocusSessionId());
        t.FocusSessionId = null;
    }

    private void ResetTimer()
    {
        var t = Store.Data.Timer;
        t.Phase = TimerPhase.Idle;
        t.EndsAt = null;
        t.StartedAt = null;
        t.PausedAt = null;
        t.RemainingSeconds = 0;
        t.DurationSeconds = 0;
        t.FocusSessionId = null;
        t.CompletedIntervals.Clear();
        t.OwnerClientId = "";
    }

    private async Task Play(TimerTone tone)
    {
        if (!Store.Data.Muted)
        {
            try
            {
                _workspaceModule ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/workspace.js");
                await _workspaceModule.InvokeVoidAsync("playTone", tone.ToWireValue());
            }
            catch (JSException)
            {
                // Browser autoplay restrictions must never interrupt a timer transition.
            }
        }
    }

    public Task StartFocus() => ExecuteAsync(StartFocusCore, requiresOwnership: false);

    public Task PauseTimer() => ExecuteAsync(PauseTimerCore);

    public Task ResumeTimer() => ExecuteAsync(ResumeTimerCore);

    public Task EndFocus() => ExecuteAsync(EndFocusCore);

    public Task SkipBreak() => ExecuteAsync(SkipBreakCore);
}
