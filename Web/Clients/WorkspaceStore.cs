using System.Text.Json;
using ChrisUsher.Core.Shared;
using Microsoft.JSInterop;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace Web.Clients;

public sealed class WorkspaceStore(IJSRuntime js, HttpClient http, AuthClient auth)
{
    private const string StoragePrefix = "flowmate.workspace.v1.";
    private IJSObjectReference? _module;
    private string StorageKey => StoragePrefix + Uri.EscapeDataString(auth.Session.Sub ?? "local");
    private string PendingStorageKey => StorageKey + ".pending";
    private string RevisionStorageKey => StorageKey + ".revision";
    private string? _revision;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private WorkspaceResponse? _remoteConflict;
    private bool _pendingSync;
    public WorkspaceSnapshot Data { get; private set; } = new();
    public string ClientId { get; private set; } = "";
    public WorkspaceSyncState SyncState { get; private set; } = WorkspaceSyncState.Saved;
    public string? SyncMessage { get; private set; }
    public bool HasConflict => _remoteConflict is not null;
    public event Action? Changed;

    public async Task InitialiseAsync()
    {
        var module = await ModuleAsync();
        ClientId = await module.InvokeAsync<string>("getClientId");
        var json = await module.InvokeAsync<string?>("read", StorageKey);
        var pending = await module.InvokeAsync<string?>("read", PendingStorageKey);
        var revision = await module.InvokeAsync<string?>("read", RevisionStorageKey);
        _pendingSync = string.Equals(pending, "true", StringComparison.Ordinal);
        _revision = string.IsNullOrWhiteSpace(revision) ? null : revision;

        if (!string.IsNullOrWhiteSpace(json))
        {
            Data = JsonSerializer.Deserialize<WorkspaceSnapshot>(json, SharedCommon.JsonOptions) ?? new();
        }

        if (auth.Session.SignedIn && !_pendingSync)
        {
            try
            {
                using var request = await auth.AuthorizedRequestAsync(HttpMethod.Get, "api/v1/workspace");
                using var response = await auth.SendAsync(http, request, requiresAuthentication: true);
                response.EnsureSuccessStatusCode();
                var remote = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

                if (remote is not null)
                {
                    Data = remote.Workspace;
                    _revision = remote.Revision;
                    await PersistRevisionAsync();

                    if (Data.Projects.Count == 0)
                    {
                        Data.DisplayName = auth.Session.Name ?? "FlowMate user";
                    }
                }
            }
            catch (HttpRequestException) { /* Keep the user's local workspace available during an API outage. */ }
        }

        if (Data.Projects.Count == 0 && !auth.IsSessionExpired)
        {
            Data.Projects =
            [
                new()
                {
                    Name = "Personal",
                    Color = "#5b68e8"
                }
            ];
            Data.Tasks =
            [
                new()
                {
                    Title = "Choose one thing to focus on",
                    ProjectId = Data.Projects[0].Id,
                    Priority = TaskPriority.High,
                    PlannedToday = true
                },
                new()
                {
                    Title = "Take a short reset",
                    ProjectId = Data.Projects[0].Id,
                    Priority = TaskPriority.Medium,
                    PlannedToday = true
                }
            ];
        }

        Data.ClientId = ClientId;
        RecoverTimer();
        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        Data.ClientId = ClientId;
        await _saveGate.WaitAsync();

        try
        {
            await PersistLocalAsync();
            await SetPendingSyncAsync(true);

            if (!auth.Session.SignedIn || auth.IsSessionExpired)
            {
                SetSyncState(auth.IsSessionExpired ? WorkspaceSyncState.AuthenticationExpired : WorkspaceSyncState.Pending,
                    auth.IsSessionExpired
                        ? "Your session expired. Your local draft and timer are saved on this device; sign in again to sync."
                        : "Saved on this device. Sign in to sync this workspace.");
                Changed?.Invoke();

                return;
            }

            if (_remoteConflict is not null)
            {
                SetSyncState(WorkspaceSyncState.Conflict, "Your local draft and the latest server version both have changes. Choose which version to keep.");
                Changed?.Invoke();

                return;
            }

            var sentWorkspace = JsonSerializer.Deserialize<WorkspaceSnapshot>(JsonSerializer.Serialize(Data, SharedCommon.JsonOptions), SharedCommon.JsonOptions) ?? new();
            var sentJson = JsonSerializer.Serialize(sentWorkspace, SharedCommon.JsonOptions);
            SetSyncState(WorkspaceSyncState.Saving, "Saving your workspace…");
            using var request = await auth.AuthorizedRequestAsync(HttpMethod.Put, "api/v1/workspace");
            request.Content = JsonContent.Create(new WorkspaceSaveRequest(sentWorkspace, _revision), options: SharedCommon.JsonOptions);
            using var response = await auth.SendAsync(http, request, requiresAuthentication: true);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || auth.IsSessionExpired)
            {
                SetSyncState(WorkspaceSyncState.AuthenticationExpired, "Your session expired. Your local draft and timer are saved on this device; sign in again to sync.");
            }
            else if (response.IsSuccessStatusCode)
            {
                var saved = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

                if (saved is not null)
                {
                    _revision = saved.Revision;
                    var currentJson = JsonSerializer.Serialize(Data, SharedCommon.JsonOptions);

                    if (string.Equals(currentJson, sentJson, StringComparison.Ordinal))
                    {
                        Data = saved.Workspace;
                        Data.ClientId = ClientId;
                        await PersistLocalAsync();
                        await SetPendingSyncAsync(false);
                    }
                    else
                    {
                        await PersistRevisionAsync();
                    }
                    SetSyncState(WorkspaceSyncState.Saved, "Workspace saved.");
                }
                else
                {
                    SetSyncState(WorkspaceSyncState.Failed, "The server response was incomplete. Your local draft is safe; try saving again.");
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>(SharedCommon.JsonOptions);
                await LoadConflictAsync(error?.Code);
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.PaymentRequired)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>(SharedCommon.JsonOptions);
                SetSyncState(WorkspaceSyncState.Rejected, error?.Message ?? "This change exceeds your plan limits. Your local draft is safe; remove a project or upgrade, then retry.");
            }
            else
            {
                SetSyncState(WorkspaceSyncState.Failed, "The workspace could not be saved. Your local draft is safe; try again.");
            }
        }
        catch (HttpRequestException)
        {
            SetSyncState(WorkspaceSyncState.Offline, "You are offline. Your local draft is safe and will sync when you retry.");
        }
        finally
        {
            _saveGate.Release();
        }

        Changed?.Invoke();
    }

    public async Task PreserveForReauthenticationAsync()
    {
        await _saveGate.WaitAsync();

        try
        {
            Data.ClientId = ClientId;
            await PersistLocalAsync();
            await SetPendingSyncAsync(true);
            SetSyncState(WorkspaceSyncState.AuthenticationExpired, "Your session expired. Your local draft and timer are saved on this device; sign in again to sync.");
        }
        finally
        {
            _saveGate.Release();
        }

        Changed?.Invoke();
    }

    public async Task UseServerVersionAsync()
    {
        if (_remoteConflict is null)
        {
            return;
        }

        Data = _remoteConflict.Workspace;
        Data.ClientId = ClientId;
        _revision = _remoteConflict.Revision;
        _remoteConflict = null;
        SetSyncState(WorkspaceSyncState.Saved, "Using the latest server version.");
        await PersistLocalAsync();
        await PersistRevisionAsync();
        await SetPendingSyncAsync(false);
        Changed?.Invoke();
    }

    public async Task KeepLocalVersionAsync()
    {
        if (_remoteConflict is null)
        {
            return;
        }

        _revision = _remoteConflict.Revision;
        _remoteConflict = null;
        await PersistRevisionAsync();
        await SetPendingSyncAsync(true);
        SetSyncState(WorkspaceSyncState.Saving, "Retrying your local draft against the latest revision.");
        await SaveAsync();
    }

    private async Task LoadConflictAsync(string? errorCode)
    {
        using var refresh = await auth.AuthorizedRequestAsync(HttpMethod.Get, "api/v1/workspace");
        using var latest = await auth.SendAsync(http, refresh, requiresAuthentication: true);

        if (latest.StatusCode == System.Net.HttpStatusCode.Unauthorized || auth.IsSessionExpired)
        {
            SetSyncState(WorkspaceSyncState.AuthenticationExpired, "Your session expired. Your local draft and timer are saved on this device; sign in again to sync.");

            return;
        }

        if (latest.IsSuccessStatusCode)
        {
            _remoteConflict = await latest.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);
        }

        var timerConflict = string.Equals(errorCode, "timer_conflict", StringComparison.Ordinal);
        SetSyncState(WorkspaceSyncState.Conflict, _remoteConflict is null
            ? "The server rejected this save. Your local draft is safe, but the latest server version could not be loaded. Reconnect and retry."
            : timerConflict
                ? "A timer is active on another device. Your local draft is safe; choose the server version or stop the conflicting timer before retrying."
                : "This workspace changed on another device. Your local draft is safe; choose which version to keep.");
    }

    private async Task PersistLocalAsync()
    {
        var json = JsonSerializer.Serialize(Data, SharedCommon.JsonOptions);
        await (await ModuleAsync()).InvokeVoidAsync("write", StorageKey, json);
    }

    private async Task PersistRevisionAsync() =>
        await (await ModuleAsync()).InvokeVoidAsync("write", RevisionStorageKey, _revision ?? "");

    private async Task SetPendingSyncAsync(bool pending)
    {
        _pendingSync = pending;
        await (await ModuleAsync()).InvokeVoidAsync("write", PendingStorageKey, pending ? "true" : "false");
        await PersistRevisionAsync();
    }

    private void SetSyncState(WorkspaceSyncState state, string message)
    {
        SyncState = state;
        SyncMessage = message;
    }

    public ProjectRecord? Project(string id) => Data.Projects.FirstOrDefault(p => p.Id == id);
    public TaskRecord? Task(string? id) => Data.Tasks.FirstOrDefault(t => t.Id == id);

    public async Task<CoachResponse?> AskCoachAsync(string prompt, string? conversationId, CancellationToken cancellationToken = default)
    {
        if (!auth.Session.SignedIn)
        {
            return null;
        }
        using var request = await auth.AuthorizedRequestAsync(HttpMethod.Post, "api/v1/coach", cancellationToken);
        request.Content = JsonContent.Create(new CoachRequest(prompt, conversationId), options: SharedCommon.JsonOptions);
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var result = await response.Content.ReadFromJsonAsync<CoachResponse>(SharedCommon.JsonOptions, cancellationToken);

        if (result is not null)
        {
            _revision = result.Revision;
            Data.Conversations.RemoveAll(c => c.Id == result.Conversation.Id);
            Data.Conversations.Insert(0, result.Conversation);
            await (await ModuleAsync()).InvokeVoidAsync("write", StorageKey, JsonSerializer.Serialize(Data, SharedCommon.JsonOptions));
            Changed?.Invoke();
        }

        return result;
    }

    private void RecoverTimer()
    {
        var timer = Data.Timer;

        if ((timer.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak) && timer.EndsAt is { } end && (timer.OwnerClientId == ClientId || end <= DateTimeOffset.UtcNow))
        {
            var left = (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalSeconds);

            if (left > 0)
            {
                timer.RemainingSeconds = left;

                return;
            }

            if (timer.Phase == TimerPhase.Focus)
            {
                foreach (var interval in timer.CompletedIntervals)
                {
                    RecordFocus(interval.StartedAt, interval.EndedAt, timer.TaskId, timer.ProjectId, (int)(interval.EndedAt - interval.StartedAt).TotalSeconds);
                }

                if (timer.StartedAt is { } started)
                {
                    RecordFocus(started, end, timer.TaskId, timer.ProjectId, Math.Max(0, timer.DurationSeconds - timer.CompletedIntervals.Sum(i => (int)(i.EndedAt - i.StartedAt).TotalSeconds)));
                }
            }

            // A closed tab never starts the next focus session on the user's behalf.
            timer.Phase = timer.Phase == TimerPhase.Focus ? TimerPhase.Idle : TimerPhase.Idle;
            timer.EndsAt = null;
            timer.StartedAt = null;
            timer.RemainingSeconds = 0;
        }
    }

    public void RecordFocus(DateTimeOffset started, DateTimeOffset ended, string? taskId, string projectId, int plannedSeconds)
    {
        var finish = started.AddSeconds(Math.Max(0, Math.Min((ended - started).TotalSeconds, plannedSeconds)));

        if (finish <= started)
        {
            return;
        }
        TimeZoneInfo zone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(Data.TimeZone);
        }
        catch
        {
            zone = TimeZoneInfo.Local;
        }

        var cursor = started;
        var elapsed = 0d;
        var assignedMinutes = 0;

        while (cursor < finish)
        {
            var localDate = TimeZoneInfo.ConvertTime(cursor, zone).Date;
            var nextMidnight = DateTime.SpecifyKind(localDate.AddDays(1), DateTimeKind.Unspecified);

            while (zone.IsInvalidTime(nextMidnight))
            {
                nextMidnight = nextMidnight.AddMinutes(1);
            }
            var nextBoundary = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(nextMidnight, zone), TimeSpan.Zero);
            var segmentEnd = nextBoundary < finish ? nextBoundary : finish;

            if (segmentEnd <= cursor)
            {
                segmentEnd = finish;
            }
            elapsed += (segmentEnd - cursor).TotalSeconds;
            var cumulativeMinutes = (int)Math.Floor(elapsed / 60d);
            var segmentMinutes = cumulativeMinutes - assignedMinutes;

            if (segmentMinutes > 0)
            {
                Data.Sessions.Add(new() { StartedAt = cursor, EndedAt = segmentEnd, FocusMinutes = segmentMinutes, TaskId = taskId, ProjectId = projectId });
            }
            assignedMinutes = cumulativeMinutes;
            cursor = segmentEnd;
        }
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/workspace.js");
}
