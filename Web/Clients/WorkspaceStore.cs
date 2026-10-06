using System.Net.Http.Headers;
using System.Text.Json;
using ChrisUsher.Core.Shared;
using Microsoft.JSInterop;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace Web.Clients;

public sealed class WorkspaceStore(IJSRuntime js, HttpClient http, Auth0Client auth)
{
    private const string StoragePrefix = "flowmate.workspace.v1.";
    private IJSObjectReference? _module;
    private string _storageAccountId = "local";
    private bool _accountBound;
    private string StorageKey => StoragePrefix + Uri.EscapeDataString(_storageAccountId);
    private string PendingStorageKey => StorageKey + ".pending";
    private string RevisionStorageKey => StorageKey + ".revision";
    private string? _revision;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private WorkspaceResponse? _remoteConflict;
    private bool _pendingSync;
    private bool _invalidPendingDraft;
    private bool _remoteLoadFailed;
    public WorkspaceSnapshot Data { get; private set; } = new();
    public string ClientId { get; private set; } = "";
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow + _serverClockOffset;
    public WorkspaceSyncState SyncState { get; private set; } = WorkspaceSyncState.Saved;
    public string? SyncMessage { get; private set; }
    public bool HasConflict => _remoteConflict is not null;
    public bool RequiresRemoteReload => _remoteLoadFailed;
    public event Action? Changed;
    private TimeSpan _serverClockOffset;

    public async Task InitialiseAsync()
    {
        BindAccount();
        var module = await ModuleAsync();
        ClientId = await module.InvokeAsync<string>("getClientId");
        var json = await module.InvokeAsync<string?>("read", StorageKey);
        var pending = await module.InvokeAsync<string?>("read", PendingStorageKey);
        var revision = await module.InvokeAsync<string?>("read", RevisionStorageKey);
        _revision = string.IsNullOrWhiteSpace(revision) ? null : revision;
        var cache = ReadWorkspaceCache(json);

        if (cache is not null)
        {
            if (!string.Equals(cache.AccountId, _storageAccountId, StringComparison.Ordinal))
            {
                _invalidPendingDraft = true;
            }
            else
            {
                Data = cache.Workspace;
                _revision = cache.Revision;
                _pendingSync = cache.Pending;
                _serverClockOffset = cache.ServerClockOffset;
            }
        }
        else if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                Data = JsonSerializer.Deserialize<WorkspaceSnapshot>(json, SharedCommon.JsonOptions) ?? new();
            }
            catch (JsonException)
            {
                _invalidPendingDraft = true;
            }
        }

        if (cache is null && string.Equals(pending, "true", StringComparison.Ordinal))
        {
            // Migrate the original pending flag while keeping its local snapshot and revision.
            _pendingSync = true;
        }
        else if (cache is null && !string.IsNullOrWhiteSpace(pending) && !string.Equals(pending, "false", StringComparison.Ordinal))
        {
            try
            {
                var draft = JsonSerializer.Deserialize<PendingWorkspaceDraft>(pending, SharedCommon.JsonOptions);

                if (draft is not null && string.Equals(draft.AccountId, _storageAccountId, StringComparison.Ordinal))
                {
                    Data = draft.Workspace;
                    _revision = draft.Revision;
                    _pendingSync = true;
                }
                else
                {
                    _invalidPendingDraft = true;
                }
            }
            catch (JsonException)
            {
                _invalidPendingDraft = true;
            }
        }

        if (_invalidPendingDraft)
        {
            SetSyncState(WorkspaceSyncState.Failed, "A saved draft could not be matched to this account, so it was left untouched. Switch to its original account or contact support.");
            Changed?.Invoke();

            return;
        }

        if (auth.Session.SignedIn && !string.IsNullOrWhiteSpace(auth.Session.AccessToken) && !_pendingSync)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/workspace");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
                using var response = await auth.SendAsync(http, request, requiresAuthentication: true);
                response.EnsureSuccessStatusCode();
                var remote = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

                if (remote is not null && remote.Workspace is not null && !string.IsNullOrWhiteSpace(remote.Revision))
                {
                    CalibrateClock(response);
                    Data = remote.Workspace;
                    _revision = remote.Revision;
                    await PersistRevisionAsync();

                    if (Data.Projects.Count == 0)
                    {
                        Data.DisplayName = auth.Session.Name ?? "FlowMate user";
                    }
                }
                else
                {
                    _remoteLoadFailed = true;
                    SetSyncState(WorkspaceSyncState.Failed, "The server returned an incomplete workspace. Your local copy is safe; retry loading the server workspace.");
                }
            }
            catch (HttpRequestException)
            {
                _remoteLoadFailed = true;
                SetSyncState(WorkspaceSyncState.Offline, "The latest workspace could not be loaded. Your local copy is safe; reconnect and retry.");
            }
            catch (JsonException)
            {
                _remoteLoadFailed = true;
                SetSyncState(WorkspaceSyncState.Failed, "The server returned an unreadable workspace. Your local draft is still available; retry loading the server workspace.");
            }
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

        if (_remoteLoadFailed)
        {
            await PersistLocalAsync();
            Changed?.Invoke();

            return;
        }

        await SaveAsync();
    }

    public async Task SaveAsync()
    {
        BindAccount();

        if (_invalidPendingDraft || !string.Equals(_storageAccountId, auth.Session.Sub ?? "local", StringComparison.Ordinal))
        {
            SetSyncState(_invalidPendingDraft ? WorkspaceSyncState.Failed : WorkspaceSyncState.Pending,
                _invalidPendingDraft
                    ? "This saved draft is protected until its account can be confirmed."
                    : "Your account changed. This draft is saved for its original account; switch back to sync it.");
            Changed?.Invoke();

            return;
        }

        Data.ClientId = ClientId;
        await _saveGate.WaitAsync();

        try
        {
            if (!string.Equals(_storageAccountId, auth.Session.Sub ?? "local", StringComparison.Ordinal))
            {
                SetSyncState(WorkspaceSyncState.Pending, "Your account changed. This draft is saved for its original account; switch back to sync it.");
                Changed?.Invoke();

                return;
            }

            await SetPendingSyncAsync(true);

            if (_remoteLoadFailed)
            {
                SetSyncState(WorkspaceSyncState.Failed, "Your draft is saved on this device. Load the latest server workspace before retrying the save.");
                Changed?.Invoke();

                return;
            }

            if (!auth.Session.SignedIn || string.IsNullOrWhiteSpace(auth.Session.AccessToken))
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
            using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1/workspace")
            {
                Content = JsonContent.Create(new WorkspaceSaveRequest(sentWorkspace, _revision), options: SharedCommon.JsonOptions)
            };
            var requestAccountId = auth.Session.Sub ?? "local";
            var accessToken = auth.Session.AccessToken;

            if (!string.Equals(requestAccountId, _storageAccountId, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(accessToken))
            {
                SetSyncState(WorkspaceSyncState.Pending, "Your account changed. This draft is saved for its original account; switch back to sync it.");
                Changed?.Invoke();

                return;
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await auth.SendAsync(http, request, requiresAuthentication: true);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || auth.IsSessionExpired)
            {
                SetSyncState(WorkspaceSyncState.AuthenticationExpired, "Your session expired. Your local draft and timer are saved on this device; sign in again to sync.");
            }
            else if (response.IsSuccessStatusCode)
            {
                var saved = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

                if (saved is not null && saved.Workspace is not null && !string.IsNullOrWhiteSpace(saved.Revision))
                {
                    CalibrateClock(response);
                    _revision = saved.Revision;
                    var currentJson = JsonSerializer.Serialize(Data, SharedCommon.JsonOptions);

                    if (string.Equals(currentJson, sentJson, StringComparison.Ordinal))
                    {
                        Data = saved.Workspace;
                        Data.ClientId = ClientId;
                        await SetPendingSyncAsync(false);
                        SetSyncState(WorkspaceSyncState.Saved, "Workspace saved.");
                    }
                    else
                    {
                        await SetPendingSyncAsync(true);
                        SetSyncState(WorkspaceSyncState.Pending, "Your latest edits are saved on this device and waiting to sync.");
                    }
                }
                else
                {
                    SetSyncState(WorkspaceSyncState.Failed, "The server response was incomplete. Your local draft is safe; try saving again.");
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<ApiError>(SharedCommon.JsonOptions);
                await LoadConflictAsync(error?.Code, requestAccountId, accessToken);
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
        catch (OperationCanceledException)
        {
            SetSyncState(WorkspaceSyncState.Offline, "The save was interrupted. Your local draft is safe; reconnect and retry.");
        }
        catch (JsonException)
        {
            SetSyncState(WorkspaceSyncState.Failed, "The server returned an unreadable save response. Your local draft is safe; retry the save.");
        }
        catch (JSException)
        {
            SetSyncState(WorkspaceSyncState.Failed, "This browser could not save your local draft. Check available storage before editing further.");
        }
        finally
        {
            _saveGate.Release();
        }

        Changed?.Invoke();
    }

    public async Task RetrySaveAsync()
    {
        BindAccount();

        if (!_remoteLoadFailed)
        {
            await SaveAsync();

            return;
        }

        if (!string.Equals(_storageAccountId, auth.Session.Sub ?? "local", StringComparison.Ordinal))
        {
            SetSyncState(WorkspaceSyncState.Pending, "Your account changed. This draft is saved for its original account; switch back to sync it.");
            Changed?.Invoke();

            return;
        }

        await _saveGate.WaitAsync();

        try
        {
            if (!auth.Session.SignedIn || string.IsNullOrWhiteSpace(auth.Session.AccessToken))
            {
                SetSyncState(WorkspaceSyncState.Pending, "Saved on this device. Sign in to load and sync the latest workspace.");

                return;
            }

            var requestAccountId = auth.Session.Sub ?? "local";
            var accessToken = auth.Session.AccessToken;

            if (!string.Equals(requestAccountId, _storageAccountId, StringComparison.Ordinal))
            {
                SetSyncState(WorkspaceSyncState.Pending, "Your account changed. This draft is saved for its original account; switch back to sync it.");

                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/workspace");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await auth.SendAsync(http, request, requiresAuthentication: true);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized || auth.IsSessionExpired)
            {
                SetSyncState(WorkspaceSyncState.AuthenticationExpired, "Your session expired. Sign in again to load and sync the latest workspace.");

                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                SetSyncState(WorkspaceSyncState.Offline, "The latest workspace could not be loaded. Your local copy is safe; reconnect and retry.");

                return;
            }

            var remote = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

            if (remote is null || remote.Workspace is null || string.IsNullOrWhiteSpace(remote.Revision))
            {
                SetSyncState(WorkspaceSyncState.Failed, "The server returned an incomplete workspace. Your local draft is still available; retry loading.");

                return;
            }

            CalibrateClock(response);

            _remoteLoadFailed = false;

            if (_pendingSync)
            {
                _remoteConflict = remote;
                SetSyncState(WorkspaceSyncState.Conflict, "The server workspace loaded while your local draft had changes. Choose which version to keep.");

                return;
            }

            Data = remote.Workspace;
            Data.ClientId = ClientId;
            _revision = remote.Revision;
            RecoverTimer();
            await PersistLocalAsync();
            SetSyncState(WorkspaceSyncState.Saved, "Workspace loaded.");
        }
        catch (HttpRequestException)
        {
            SetSyncState(WorkspaceSyncState.Offline, "The latest workspace could not be loaded. Your local copy is safe; reconnect and retry.");
        }
        catch (OperationCanceledException)
        {
            SetSyncState(WorkspaceSyncState.Offline, "Loading the latest workspace was interrupted. Your local copy is safe; reconnect and retry.");
        }
        catch (JsonException)
        {
            SetSyncState(WorkspaceSyncState.Failed, "The server returned an unreadable workspace. Your local draft is still available; retry loading.");
        }
        catch (JSException)
        {
            SetSyncState(WorkspaceSyncState.Failed, "This browser could not preserve the local workspace. Check available storage before retrying.");
        }
        finally
        {
            _saveGate.Release();
            Changed?.Invoke();
        }
    }

    public async Task PreserveForReauthenticationAsync()
    {
        await _saveGate.WaitAsync();

        try
        {
            Data.ClientId = ClientId;
            await SetPendingSyncAsync(true);
            SetSyncState(WorkspaceSyncState.AuthenticationExpired, "Your session expired. Your local draft and timer are saved on this device; sign in again to sync.");
        }
        catch (JSException)
        {
            SetSyncState(WorkspaceSyncState.Failed, "This browser could not preserve the draft locally. Check available storage before continuing.");
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

    private async Task LoadConflictAsync(string? errorCode, string requestAccountId, string accessToken)
    {
        if (!string.Equals(requestAccountId, auth.Session.Sub ?? "local", StringComparison.Ordinal))
        {
            SetSyncState(WorkspaceSyncState.Pending, "Your account changed while saving. The local draft is still saved for its original account.");

            return;
        }

        using var refresh = new HttpRequestMessage(HttpMethod.Get, "api/v1/workspace");
        refresh.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var latest = await auth.SendAsync(http, refresh, requiresAuthentication: true);

        if (latest.StatusCode == System.Net.HttpStatusCode.Unauthorized || auth.IsSessionExpired)
        {
            SetSyncState(WorkspaceSyncState.AuthenticationExpired, "Your session expired. Your local draft and timer are saved on this device; sign in again to sync.");

            return;
        }

        if (latest.IsSuccessStatusCode)
        {
            CalibrateClock(latest);
            _remoteConflict = await latest.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

            if (string.Equals(errorCode, "timer_conflict", StringComparison.Ordinal) && _remoteConflict?.Workspace is { } remoteWorkspace)
            {
                Data.Timer = remoteWorkspace.Timer;
            }
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
        var json = JsonSerializer.Serialize(new WorkspaceCacheRecord(_storageAccountId, _revision, _pendingSync, Data, DateTimeOffset.UtcNow, _serverClockOffset), SharedCommon.JsonOptions);
        await (await ModuleAsync()).InvokeVoidAsync("write", StorageKey, json);
    }

    private async Task PersistRevisionAsync()
    {
        await PersistLocalAsync();
        await (await ModuleAsync()).InvokeVoidAsync("write", RevisionStorageKey, _revision ?? "");
    }

    private async Task SetPendingSyncAsync(bool pending)
    {
        _pendingSync = pending;
        await PersistLocalAsync();
        var value = pending ? "true" : "false";
        await (await ModuleAsync()).InvokeVoidAsync("write", PendingStorageKey, value);
        await (await ModuleAsync()).InvokeVoidAsync("write", RevisionStorageKey, _revision ?? "");
    }

    private void SetSyncState(WorkspaceSyncState state, string message)
    {
        SyncState = state;
        SyncMessage = message;
    }

    private static WorkspaceCacheRecord? ReadWorkspaceCache(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, "AccountId", StringComparison.OrdinalIgnoreCase)) ||
                !document.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, "Workspace", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var cache = JsonSerializer.Deserialize<WorkspaceCacheRecord>(json, SharedCommon.JsonOptions);

            return cache is null || cache.Workspace is null || string.IsNullOrWhiteSpace(cache.AccountId) ? null : cache;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record WorkspaceCacheRecord(string AccountId, string? Revision, bool Pending, WorkspaceSnapshot Workspace, DateTimeOffset UpdatedAt, TimeSpan ServerClockOffset);
    private sealed record PendingWorkspaceDraft(string AccountId, string? Revision, WorkspaceSnapshot Workspace, DateTimeOffset UpdatedAt);

    public ProjectRecord? Project(string id) => Data.Projects.FirstOrDefault(p => p.Id == id);
    public TaskRecord? Task(string? id) => Data.Tasks.FirstOrDefault(t => t.Id == id);

    public async Task<CoachResponse?> AskCoachAsync(string prompt, string? conversationId, CancellationToken cancellationToken = default)
    {
        BindAccount();
        var accountId = auth.Session.Sub ?? "local";
        var accessToken = auth.Session.AccessToken;

        if (!auth.Session.SignedIn || string.IsNullOrWhiteSpace(accessToken) ||
            !string.Equals(_storageAccountId, accountId, StringComparison.Ordinal))
        {
            return null;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/coach")
        {
            Content = JsonContent.Create(new CoachRequest(prompt, conversationId), options: SharedCommon.JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await auth.SendAsync(http, request, requiresAuthentication: true, cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var result = await response.Content.ReadFromJsonAsync<CoachResponse>(SharedCommon.JsonOptions, cancellationToken);

        if (result is not null)
        {
            if (!string.Equals(_storageAccountId, auth.Session.Sub ?? "local", StringComparison.Ordinal))
            {
                return null;
            }

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

        if ((timer.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak) && timer.EndsAt is { } end && (timer.OwnerClientId == ClientId || end <= UtcNow))
        {
            var left = (int)Math.Ceiling((end - UtcNow).TotalSeconds);

            if (left > 0)
            {
                timer.RemainingSeconds = left;

                return;
            }

            if (timer.Phase == TimerPhase.Focus)
            {
                RecordFocusIntervals(CompletedTimerIntervals(timer, end), timer.TaskId, timer.ProjectId, timer.DurationSeconds, timer.EnsureFocusSessionId());
                timer.CompletedPomodoros++;
            }

            // A closed tab never starts the next focus session on the user's behalf.
            timer.Phase = TimerPhase.Idle;
            timer.EndsAt = null;
            timer.StartedAt = null;
            timer.RemainingSeconds = 0;
            timer.DurationSeconds = 0;
            timer.CompletedIntervals.Clear();
            timer.FocusSessionId = null;
            timer.OwnerClientId = "";
        }
    }

    public void RecordFocus(DateTimeOffset started, DateTimeOffset ended, string? taskId, string projectId, int plannedSeconds)
    {
        RecordFocusIntervals([new() { StartedAt = started, EndedAt = ended }], taskId, projectId, plannedSeconds);
    }

    public void RecordFocusIntervals(IReadOnlyCollection<FocusIntervalRecord> intervals, string? taskId, string projectId, int plannedSeconds, string? focusSessionId = null)
    {
        var remainingSeconds = (double)Math.Max(0, plannedSeconds);
        var elapsed = 0d;
        var assignedMinutes = 0;
        var sessionRecords = new Dictionary<string, FocusSessionRecord>(StringComparer.Ordinal);
        TimeZoneInfo zone;

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(Data.TimeZone);
        }
        catch
        {
            zone = TimeZoneInfo.Local;
        }

        foreach (var interval in intervals.OrderBy(interval => interval.StartedAt))
        {
            if (remainingSeconds <= 0)
            {
                break;
            }

            var intervalSeconds = Math.Min((interval.EndedAt - interval.StartedAt).TotalSeconds, remainingSeconds);
            var finish = interval.StartedAt.AddSeconds(Math.Max(0, intervalSeconds));

            if (finish <= interval.StartedAt)
            {
                continue;
            }

            var cursor = interval.StartedAt;

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
                    var sessionDay = TimeZoneInfo.ConvertTime(cursor, zone).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                    var sessionId = focusSessionId is null ? Guid.NewGuid().ToString("N") : $"{focusSessionId}-{sessionDay}";

                    if (sessionRecords.TryGetValue(sessionId, out var session))
                    {
                        session.EndedAt = segmentEnd;
                        session.FocusMinutes += segmentMinutes;
                    }
                    else
                    {
                        sessionRecords.Add(sessionId, new() { Id = sessionId, StartedAt = cursor, EndedAt = segmentEnd, FocusMinutes = segmentMinutes, TaskId = taskId, ProjectId = projectId });
                    }
                }
                assignedMinutes = cumulativeMinutes;
                cursor = segmentEnd;
            }

            remainingSeconds -= intervalSeconds;
        }

        foreach (var session in sessionRecords.Values.Where(session => Data.Sessions.All(existing => existing.Id != session.Id)))
        {
            Data.Sessions.Add(session);
        }
    }

    private static IReadOnlyCollection<FocusIntervalRecord> CompletedTimerIntervals(TimerSnapshot timer, DateTimeOffset end)
    {
        var intervals = timer.CompletedIntervals.ToList();

        if (timer.StartedAt is { } started && end > started)
        {
            intervals.Add(new() { StartedAt = started, EndedAt = end });
        }

        return intervals;
    }

    private void CalibrateClock(HttpResponseMessage response)
    {
        if (response.Headers.Date is { } serverDate)
        {
            _serverClockOffset = serverDate - DateTimeOffset.UtcNow;
        }
    }

    private void BindAccount()
    {
        if (_accountBound)
        {
            return;
        }

        _storageAccountId = auth.Session.Sub ?? "local";
        _accountBound = true;
    }

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/workspace.js");
}
