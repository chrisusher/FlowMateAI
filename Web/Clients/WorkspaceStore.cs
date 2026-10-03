using System.Net.Http.Headers;
using System.Text.Json;
using ChrisUsher.Core.Shared;
using Microsoft.JSInterop;
using Shared.Contracts;
using Shared.Models;

namespace Web.Clients;

public sealed class WorkspaceStore(IJSRuntime js, HttpClient http, Auth0Client auth)
{
    private const string StoragePrefix = "flowmate.workspace.v1.";
    private IJSObjectReference? _module;
    private string StorageKey => StoragePrefix + Uri.EscapeDataString(auth.Session.Sub ?? "local");
    private string? _revision;
    public WorkspaceSnapshot Data { get; private set; } = new();
    public string ClientId { get; private set; } = "";
    public event Action? Changed;

    public async Task InitialiseAsync()
    {
        var module = await ModuleAsync();
        ClientId = await module.InvokeAsync<string>("getClientId");
        var json = await module.InvokeAsync<string?>("read", StorageKey);

        if (!string.IsNullOrWhiteSpace(json))
        {
            Data = JsonSerializer.Deserialize<WorkspaceSnapshot>(json, SharedCommon.JsonOptions) ?? new();
        }

        if (auth.Session.SignedIn && !string.IsNullOrWhiteSpace(auth.Session.AccessToken))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/workspace");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
                using var response = await http.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var remote = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

                if (remote is not null)
                {
                    Data = remote.Workspace;
                    _revision = remote.Revision;

                    if (Data.Projects.Count == 0)
                    {
                        Data.DisplayName = auth.Session.Name ?? "FlowMate user";
                    }
                }
            }
            catch (HttpRequestException) { /* Keep the user's local workspace available during an API outage. */ }
        }

        if (Data.Projects.Count == 0)
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
        var json = JsonSerializer.Serialize(Data, SharedCommon.JsonOptions);
        await (await ModuleAsync()).InvokeVoidAsync("write", StorageKey, json);

        if (auth.Session.SignedIn && !string.IsNullOrWhiteSpace(auth.Session.AccessToken))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, "api/v1/workspace")
                {
                    Content = JsonContent.Create(new WorkspaceSaveRequest(Data, _revision), options: SharedCommon.JsonOptions)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
                using var response = await http.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var saved = await response.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

                    if (saved is not null)
                    {
                        _revision = saved.Revision;
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    using var refresh = new HttpRequestMessage(HttpMethod.Get, "api/v1/workspace");
                    refresh.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
                    using var latest = await http.SendAsync(refresh);

                    if (latest.IsSuccessStatusCode)
                    {
                        var remote = await latest.Content.ReadFromJsonAsync<WorkspaceResponse>(SharedCommon.JsonOptions);

                        if (remote is not null)
                        {
                            Data = remote.Workspace;
                            Data.ClientId = ClientId;
                            _revision = remote.Revision;
                            await (await ModuleAsync()).InvokeVoidAsync("write", StorageKey, JsonSerializer.Serialize(Data, SharedCommon.JsonOptions));
                        }
                    }
                }
            }
            catch (HttpRequestException) { /* Local state remains available and can sync on the next save. */ }
        }
        Changed?.Invoke();
    }

    public ProjectRecord? Project(string id) => Data.Projects.FirstOrDefault(p => p.Id == id);
    public TaskRecord? Task(string? id) => Data.Tasks.FirstOrDefault(t => t.Id == id);

    public async Task<CoachResponse?> AskCoachAsync(string prompt, string? conversationId, CancellationToken cancellationToken = default)
    {
        if (!auth.Session.SignedIn || string.IsNullOrWhiteSpace(auth.Session.AccessToken))
        {
            return null;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/coach")
        {
            Content = JsonContent.Create(new CoachRequest(prompt, conversationId), options: SharedCommon.JsonOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Session.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);

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
