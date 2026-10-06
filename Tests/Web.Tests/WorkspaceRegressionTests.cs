using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChrisUsher.Core.Shared;
using Microsoft.JSInterop;
using Shared.Contracts;
using Shared.Enums;
using Web.Clients;
using Web.Components;
using Web.Components.Features.Reports;
using Web.Components.Features.Today;
using Web.Components.Layout;
using Web.Components.Pages;

namespace Web.Tests;

public sealed class WorkspaceRegressionTests : WorkspaceComponentTest
{
    [Test]
    public async Task PausedFocusIntervalsAccumulateMinutesInsteadOfRoundingEachIntervalDown()
    {
        var now = DateTimeOffset.UtcNow;
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Timer.OwnerClientId = Store.ClientId;
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        Store.Data.Timer.DurationSeconds = 1500;
        Store.Data.Timer.StartedAt = now.AddSeconds(-30);
        Store.Data.Timer.CompletedIntervals.Add(new() { StartedAt = now.AddSeconds(-60), EndedAt = now.AddSeconds(-30) });

        await Timer.EndFocus();

        Assert.That(Store.Data.Sessions.Sum(session => session.FocusMinutes), Is.EqualTo(1));
        Assert.That(Store.Data.Sessions, Has.Exactly(1).Items);
    }

    [Test]
    public void ReplayingRecoveryForTheSameFocusSessionDoesNotDuplicateItsTotals()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-10);
        FocusIntervalRecord[] intervals =
        [
            new() { StartedAt = start, EndedAt = start.AddMinutes(3) },
            new() { StartedAt = start.AddMinutes(5), EndedAt = start.AddMinutes(7) }
        ];

        Store.RecordFocusIntervals(intervals, PlannedTask.Id, Project.Id, 300, "focus-run-one");
        Store.RecordFocusIntervals(intervals, PlannedTask.Id, Project.Id, 300, "focus-run-one");

        Assert.That(Store.Data.Sessions, Has.Exactly(1).Items);
        Assert.That(Store.Data.Sessions.Sum(session => session.FocusMinutes), Is.EqualTo(5));
    }

    [Test]
    public void LegacyTimerRecoveryIdentityIsStableAcrossTabs()
    {
        var timer = new TimerSnapshot
        {
            StartedAt = new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero),
            DurationSeconds = 1500,
            TaskId = PlannedTask.Id,
            ProjectId = Project.Id
        };

        var firstTabId = timer.EnsureFocusSessionId();
        timer.FocusSessionId = null;
        var secondTabId = timer.EnsureFocusSessionId();

        Assert.That(secondTabId, Is.EqualTo(firstTabId));
    }

    [Test]
    public void PausedFocusAccountingCarriesSecondsAcrossLocalMidnight()
    {
        Store.Data.TimeZone = "Europe/London";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(Store.Data.TimeZone);
        var beforeMidnight = new DateTime(2026, 10, 6, 23, 59, 30, DateTimeKind.Unspecified);
        var midnight = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Unspecified);
        FocusIntervalRecord[] intervals =
        [
            new() { StartedAt = new(TimeZoneInfo.ConvertTimeToUtc(beforeMidnight, zone), TimeSpan.Zero), EndedAt = new(TimeZoneInfo.ConvertTimeToUtc(midnight, zone), TimeSpan.Zero) },
            new() { StartedAt = new(TimeZoneInfo.ConvertTimeToUtc(midnight, zone), TimeSpan.Zero), EndedAt = new(TimeZoneInfo.ConvertTimeToUtc(midnight.AddSeconds(30), zone), TimeSpan.Zero) }
        ];

        Store.RecordFocusIntervals(intervals, PlannedTask.Id, Project.Id, 60, "midnight-focus");

        Assert.That(Store.Data.Sessions.Sum(session => session.FocusMinutes), Is.EqualTo(1));
        Assert.That(Store.Data.Sessions.Single().StartedAt, Is.EqualTo(new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone), TimeSpan.Zero)));
    }

    [Test]
    public async Task ClosedTabRecoveryRecordsACompletedFocusWithoutStartingAnotherOne()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-26);
        Store.Data.Timer = new()
        {
            Phase = TimerPhase.Focus,
            OwnerClientId = "test-device",
            StartedAt = startedAt,
            EndsAt = startedAt.AddMinutes(25),
            DurationSeconds = 1500,
            CompletedPomodoros = 3,
            TaskId = PlannedTask.Id,
            ProjectId = Project.Id,
            FocusSessionId = "closed-tab-focus"
        };
        const string workspaceKey = "flowmate.workspace.v1.local";
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey))
            .SetResult(JsonSerializer.Serialize(Store.Data, SharedCommon.JsonOptions));
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey + ".pending"))
            .SetResult(null);
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey + ".revision"))
            .SetResult(null);

        await Store.InitialiseAsync();

        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Idle));
        Assert.That(Store.Data.Timer.CompletedPomodoros, Is.EqualTo(4));
        Assert.That(Store.Data.Sessions.Sum(session => session.FocusMinutes), Is.EqualTo(25));
        Assert.That(Store.Data.Timer.FocusSessionId, Is.Null);
    }

    [Test]
    public async Task TimerUsesTheServerDateWhenTheDeviceClockDiffers()
    {
        await SignInAsync();
        var serverNow = DateTimeOffset.UtcNow.AddHours(-3);
        var workspace = new WorkspaceSnapshot { Projects = [Project], Tasks = [PlannedTask] };
        Api.Respond = async (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                workspace = (await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions))!.Workspace;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(workspace, "server-revision"), options: SharedCommon.JsonOptions)
            };
            response.Headers.Date = serverNow;

            return response;
        };

        await Store.InitialiseAsync();
        await Timer.StartFocus();

        Assert.That(Math.Abs((Store.Data.Timer.StartedAt!.Value - serverNow).TotalSeconds), Is.LessThan(5));
        Assert.That(Timer.SecondsLeft, Is.InRange(1498, 1500));
    }

    [Test]
    public async Task TimerStartConflictRestoresTheAccountOwnedTimer()
    {
        await SignInAsync();
        var idleWorkspace = new WorkspaceSnapshot { Projects = [Project], Tasks = [PlannedTask] };
        var remoteTimer = new WorkspaceSnapshot
        {
            Projects = [Project],
            Tasks = [PlannedTask],
            Timer = new()
            {
                Phase = TimerPhase.Focus,
                OwnerClientId = "other-tab",
                StartedAt = DateTimeOffset.UtcNow,
                EndsAt = DateTimeOffset.UtcNow.AddMinutes(20),
                DurationSeconds = 1200
            }
        };
        Api.Respond = (request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new WorkspaceResponse(idleWorkspace, "revision-1"), options: SharedCommon.JsonOptions)
        });
        await Store.InitialiseAsync();
        Api.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ApiError("timer_conflict", "A timer is active on another device."), options: SharedCommon.JsonOptions)
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(remoteTimer, "revision-2"), options: SharedCommon.JsonOptions)
            });

        await Timer.StartFocus();

        Assert.That(Store.Data.Timer.OwnerClientId, Is.EqualTo("other-tab"));
        Assert.That(Timer.CanControlTimer, Is.False);
        Assert.That(Store.SyncMessage, Does.Contain("timer is active on another device"));
    }

    [Test]
    public async Task ExpiredFocusStillTransitionsWhenTheBrowserRejectsItsCompletionSound()
    {
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Timer.OwnerClientId = Store.ClientId;
        Store.Data.Timer.StartedAt = DateTimeOffset.UtcNow.AddSeconds(-61);
        Store.Data.Timer.EndsAt = DateTimeOffset.UtcNow.AddSeconds(1);
        Store.Data.Timer.DurationSeconds = 60;
        WorkspaceModule.SetupVoid("playTone", _ => true).SetException(new JSException("Audio playback is not allowed."));

        PreserveWorkspaceForInitialization();
        var cut = Render<App>();

        try
        {
            cut.WaitForState(() => Store.Data.Timer.Phase == TimerPhase.ShortBreak, TimeSpan.FromSeconds(4));
            cut.WaitForElement(".timer-completion", TimeSpan.FromSeconds(4));
            Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.ShortBreak));
            Assert.That(cut.Find(".timer-completion").GetAttribute("role"), Is.EqualTo("status"));
            Assert.That(Content(cut.Find(".timer-completion")), Does.Contain("5-minute short break"));
        }
        finally
        {
            await Timer.StopClockAsync();
        }
    }

    [TestCase(0, TimerPhase.ShortBreak, 300)]
    [TestCase(3, TimerPhase.LongBreak, 900)]
    public async Task FocusExpiryRecordsOnceAndStartsTheCorrectBreakOffTheTodayPage(int completed, TimerPhase nextPhase, int duration)
    {
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Timer.OwnerClientId = "test-device";
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        Store.Data.Timer.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        Store.Data.Timer.EndsAt = DateTimeOffset.UtcNow.AddSeconds(1);
        Store.Data.Timer.DurationSeconds = 61;
        Store.Data.Timer.CompletedPomodoros = completed;
        PreserveWorkspaceForInitialization();
        Navigation.NavigateTo("tasks");
        var cut = Render<App>();
        cut.WaitForState(() => Store.Data.Timer.Phase == nextPhase, TimeSpan.FromSeconds(4));
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(nextPhase));
        Assert.That(Content(cut.Find("h1")), Is.EqualTo("All tasks"));
        Assert.That(Store.Data.Timer.DurationSeconds, Is.EqualTo(duration));
        Assert.That(Store.Data.Timer.CompletedPomodoros, Is.EqualTo(completed + 1));
        Assert.That(Store.Data.Sessions.Single().FocusMinutes, Is.EqualTo(1));
        await Timer.StopClockAsync();
        Assert.That(Store.Data.Sessions, Has.Exactly(1).Items);
    }

    [Test]
    public async Task BreakExpiryStartsFocusWithoutRecordingAnotherSession()
    {
        Store.Data.Timer.Phase = TimerPhase.ShortBreak;
        Store.Data.Timer.OwnerClientId = "test-device";
        Store.Data.Timer.StartedAt = DateTimeOffset.UtcNow;
        Store.Data.Timer.EndsAt = DateTimeOffset.UtcNow.AddSeconds(1);
        Store.Data.Timer.DurationSeconds = 1;
        WorkspaceModule.SetupVoid("playTone", _ => true).SetException(new JSException("Audio playback is not allowed."));
        PreserveWorkspaceForInitialization();
        var cut = Render<WorkspaceLayout>();
        cut.WaitForState(() => Store.Data.Timer.Phase == TimerPhase.Focus, TimeSpan.FromSeconds(4));
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.Data.Timer.DurationSeconds, Is.EqualTo(1500));
        Assert.That(Store.Data.Sessions, Is.Empty);
        Assert.That(Timer.CompletionMessage, Does.Contain("Break complete"));
        await Timer.StopClockAsync();
    }

    [Test]
    public async Task SaveConflictPreservesTheLocalDraftUntilTheUserChoosesTheServerVersion()
    {
        await SignInAsync();
        var cut = Render<FocusStatsCard>();
        var latest = new WorkspaceSnapshot
        {
            DisplayName = "Latest workspace",
            Projects = [Project],
            Tasks = [PlannedTask],
            Sessions = [new() { ProjectId = Project.Id, StartedAt = DateTimeOffset.UtcNow, EndedAt = DateTimeOffset.UtcNow.AddMinutes(60), FocusMinutes = 60 }]
        };
        Api.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ApiError("revision_conflict", "The workspace changed."), options: SharedCommon.JsonOptions)
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(latest, "latest-revision"), options: SharedCommon.JsonOptions)
            });
        await cut.InvokeAsync(Store.SaveAsync);
        cut.WaitForState(() => Store.HasConflict);
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Alex Morgan"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Conflict));
        Assert.That(Store.SyncMessage, Does.Contain("local draft is safe"));
        await cut.InvokeAsync(Store.UseServerVersionAsync);
        cut.WaitForState(() => cut.FindAll(".focus-total strong").Any(element => Content(element) == "60m"));
        Assert.That(Content(cut.Find(".focus-total strong")), Is.EqualTo("60m"));
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Latest workspace"));
    }

    [Test]
    public async Task KeepingTheLocalDraftRetriesAgainstTheLatestServerRevision()
    {
        await SignInAsync();
        var remote = new WorkspaceSnapshot { DisplayName = "Remote edit", Projects = [Project] };
        WorkspaceSaveRequest? retry = null;
        var puts = 0;
        Api.Respond = async (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                puts++;

                if (puts == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = JsonContent.Create(new ApiError("revision_conflict", "The workspace changed."), options: SharedCommon.JsonOptions)
                    };
                }

                retry = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new WorkspaceResponse(retry!.Workspace, "accepted-revision"), options: SharedCommon.JsonOptions)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(remote, "latest-revision"), options: SharedCommon.JsonOptions)
            };
        };

        await Store.SaveAsync();
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Alex Morgan"));
        await Store.KeepLocalVersionAsync();

        Assert.That(puts, Is.EqualTo(2));
        Assert.That(retry!.Revision, Is.EqualTo("latest-revision"));
        Assert.That(retry.Workspace.DisplayName, Is.EqualTo("Alex Morgan"));
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Alex Morgan"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Saved));
    }

    [Test]
    public async Task UnauthorizedWorkspaceSaveExpiresTheSessionAndKeepsTheLocalDraft()
    {
        await SignInAsync();
        Store.Data.DisplayName = "Draft kept through reauthentication";
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(new ApiError("unauthorized", "The access token expired."), options: SharedCommon.JsonOptions)
        });

        await Store.SaveAsync();

        Assert.That(Auth.IsSessionExpired, Is.True);
        Assert.That(Auth.Session.Sub, Is.EqualTo("test-user"));
        Assert.That(Auth.Session.AccessToken, Is.Null);
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Draft kept through reauthentication"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.AuthenticationExpired));
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/workspace" }));
        Assert.That(JSInterop.Invocations.Any(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user.pending") &&
            Equals(call.Arguments[1], "true")), Is.True);
    }

    [Test]
    public async Task WorkspaceOutageKeepsLocalDraftPendingUntilAnExplicitRetrySucceeds()
    {
        await SignInAsync();
        Store.Data.DisplayName = "Offline draft";
        Api.Respond = (_, _) => throw new HttpRequestException("Network unavailable");

        await Store.SaveAsync();

        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Offline));
        Assert.That(JSInterop.Invocations.Any(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user.pending") &&
            Equals(call.Arguments[1], "true")), Is.True);

        Api.Respond = async (request, _) =>
        {
            var body = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(body!.Workspace, "reconnected-revision"), options: SharedCommon.JsonOptions)
            };
        };

        await Store.SaveAsync();

        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Saved));
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Offline draft"));
        Assert.That(JSInterop.Invocations.Any(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user.pending") && Equals(call.Arguments[1], "false")), Is.True);
    }

    [Test]
    public async Task UnreadableStartupWorkspaceWaitsForExplicitReloadBeforeSavingLocalEdits()
    {
        await SignInAsync();
        const string workspaceKey = "flowmate.workspace.v1.test-user";
        var cached = new WorkspaceSnapshot
        {
            DisplayName = "Cached workspace",
            Projects = [new() { Id = "cached-project", Name = "Cached project" }]
        };
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey))
            .SetResult(JsonSerializer.Serialize(cached, SharedCommon.JsonOptions));
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey + ".revision"))
            .SetResult("cached-revision");
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ invalid workspace response")
        });

        await Store.InitialiseAsync();

        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/workspace" }));
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Cached workspace"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Failed));
        Assert.That(Store.RequiresRemoteReload, Is.True);

        Store.Data.DisplayName = "Local edit after read failure";
        await Store.SaveAsync();

        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/workspace" }));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Failed));

        var latest = new WorkspaceSnapshot
        {
            DisplayName = "Latest remote workspace",
            Projects = [new() { Id = "remote-project", Name = "Remote project" }]
        };
        WorkspaceSaveRequest? localRetry = null;
        Api.Respond = async (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new WorkspaceResponse(latest, "latest-revision"), options: SharedCommon.JsonOptions)
                };
            }

            localRetry = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(localRetry!.Workspace, "saved-revision"), options: SharedCommon.JsonOptions)
            };
        };

        await Store.RetrySaveAsync();

        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Conflict));
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Local edit after read failure"));
        await Store.KeepLocalVersionAsync();
        Assert.That(localRetry!.Revision, Is.EqualTo("latest-revision"));
        Assert.That(localRetry.Workspace.DisplayName, Is.EqualTo("Local edit after read failure"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Saved));
    }

    [Test]
    public async Task SignedOutWorkspaceIsReportedAsPendingRatherThanOffline()
    {
        await Store.SaveAsync();

        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Pending));
        Assert.That(Store.SyncMessage, Does.Contain("Saved on this device"));
        Assert.That(Api.Paths, Is.Empty);
    }

    [Test]
    public async Task AlreadyExpiredAccessTokenIsRejectedBeforeAnAuthenticatedRequestIsSent()
    {
        Configuration["Auth0:Domain"] = "auth.example.test";
        Configuration["Auth0:ClientId"] = "test-client";
        Configuration["Auth0:Audience"] = "test-api";
        AuthModule.Setup<AuthSession>("initialize", _ => true).SetResult(new()
        {
            Configured = true,
            SignedIn = true,
            SessionExpired = false,
            Sub = "test-user",
            AccessToken = "expired-token",
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds()
        });
        await Auth.InitialiseAsync();

        await Store.SaveAsync();

        Assert.That(Auth.IsSessionExpired, Is.True);
        Assert.That(Api.Paths, Is.Empty);
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.AuthenticationExpired));
    }

    [Test]
    public async Task ReauthenticationRestoresPendingWorkspaceUsingItsSavedRevision()
    {
        await SignInAsync();
        const string workspaceKey = "flowmate.workspace.v1.test-user";
        var draft = new WorkspaceSnapshot
        {
            DisplayName = "Restored after reauthentication",
            Projects = [new() { Id = "saved-project", Name = "Saved project" }],
            Tasks = [new() { Id = "saved-task", Title = "Saved task", ProjectId = "saved-project" }],
            Timer = new()
            {
                Phase = TimerPhase.Paused,
                OwnerClientId = "test-device",
                RemainingSeconds = 120
            }
        };
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey))
            .SetResult(JsonSerializer.Serialize(draft, SharedCommon.JsonOptions));
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey + ".pending"))
            .SetResult(JsonSerializer.Serialize(new
            {
                AccountId = "test-user",
                Revision = "saved-revision",
                Workspace = draft,
                UpdatedAt = DateTimeOffset.UtcNow
            }, SharedCommon.JsonOptions));
        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], workspaceKey + ".revision"))
            .SetResult("saved-revision");
        WorkspaceSaveRequest? restored = null;
        Api.Respond = async (request, _) =>
        {
            restored = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(restored!.Workspace, "accepted-revision"), options: SharedCommon.JsonOptions)
            };
        };

        await Store.InitialiseAsync();

        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/workspace" }));
        Assert.That(restored!.Revision, Is.EqualTo("saved-revision"));
        Assert.That(restored.Workspace.DisplayName, Is.EqualTo("Restored after reauthentication"));
        Assert.That(restored.Workspace.Timer.Phase, Is.EqualTo(TimerPhase.Paused));
        Assert.That(Store.Data.Tasks.Single().Title, Is.EqualTo("Saved task"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Saved));
    }

    [Test]
    public async Task WorkspaceDraftIsNeverSavedUnderAnotherSignedInAccount()
    {
        await SignInAsync();
        Store.Data.DisplayName = "Account one draft";
        await Store.SaveAsync();
        Api.Paths.Clear();
        Auth.Session.Sub = "account-two";
        Auth.Session.AccessToken = "account-two-token";

        await Store.SaveAsync();

        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Pending));
        Assert.That(Api.Paths, Is.Empty);
        Assert.That(JSInterop.Invocations.Any(call =>
            call.Identifier == "write" && call.Arguments[0]?.ToString()?.Contains("account-two", StringComparison.Ordinal) == true), Is.False);

        var otherWorkspace = new WorkspaceSnapshot
        {
            DisplayName = "Account two workspace",
            Projects = [new() { Id = "account-two-project", Name = "Account two project" }]
        };
        WorkspaceSaveRequest? sent = null;
        Api.Respond = async (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new WorkspaceResponse(otherWorkspace, "account-two-revision"), options: SharedCommon.JsonOptions)
                };
            }

            sent = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(sent!.Workspace, "account-two-saved-revision"), options: SharedCommon.JsonOptions)
            };
        };
        var accountTwoStore = new WorkspaceStore(Services.GetRequiredService<IJSRuntime>(), Services.GetRequiredService<HttpClient>(), Auth);

        await accountTwoStore.InitialiseAsync();

        Assert.That(sent!.Workspace.DisplayName, Is.EqualTo("Account two workspace"));
        Assert.That(sent.Revision, Is.EqualTo("account-two-revision"));
        Assert.That(JSInterop.Invocations.Any(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.account-two")), Is.True);
    }

    [Test]
    public async Task PlanRejectedSaveRemainsPendingAndDoesNotReportCloudSuccess()
    {
        await SignInAsync();
        Store.Data.DisplayName = "Draft after plan rejection";
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.PaymentRequired)
        {
            Content = JsonContent.Create(new ApiError("project_limit", "Your plan has reached its project limit."), options: SharedCommon.JsonOptions)
        });

        await Store.SaveAsync();

        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Rejected));
        Assert.That(Store.SyncMessage, Does.Contain("project limit"));
        var cached = JSInterop.Invocations.Last(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user"))
            .Arguments[1]!.ToString();
        using var cachedJson = JsonDocument.Parse(cached!);
        Assert.That(Property(cachedJson.RootElement, "AccountId").GetString(), Is.EqualTo("test-user"));
        Assert.That(Property(cachedJson.RootElement, "Pending").GetBoolean(), Is.True);
        Assert.That(Property(Property(cachedJson.RootElement, "Workspace"), "DisplayName").GetString(), Is.EqualTo("Draft after plan rejection"));
    }

    [Test]
    public async Task OfflineDraftEnvelopeRestoresTheLatestEditAndItsAccountRevisionAfterReload()
    {
        await SignInAsync();
        Api.Respond = (_, _) => throw new HttpRequestException("Network unavailable");
        Store.Data.DisplayName = "First offline edit";
        await Store.SaveAsync();
        Store.Data.DisplayName = "Second offline edit";
        await Store.SaveAsync();

        var cached = JSInterop.Invocations.Last(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user"))
            .Arguments[1]!.ToString();
        using var cachedJson = JsonDocument.Parse(cached!);
        Assert.That(Property(cachedJson.RootElement, "AccountId").GetString(), Is.EqualTo("test-user"));
        Assert.That(Property(cachedJson.RootElement, "Pending").GetBoolean(), Is.True);
        Assert.That(Property(Property(cachedJson.RootElement, "Workspace"), "DisplayName").GetString(), Is.EqualTo("Second offline edit"));
        Assert.That(JSInterop.Invocations.Last(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user.pending"))
            .Arguments[1], Is.EqualTo("true"));

        WorkspaceModule.Setup<string?>("read", args => Equals(args.Arguments[0], "flowmate.workspace.v1.test-user"))
            .SetResult(cached);
        Api.Paths.Clear();
        WorkspaceSaveRequest? restored = null;
        Api.Respond = async (request, _) =>
        {
            restored = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(restored!.Workspace, "reconnected-revision"), options: SharedCommon.JsonOptions)
            };
        };
        var reloadedStore = new WorkspaceStore(Services.GetRequiredService<IJSRuntime>(), Services.GetRequiredService<HttpClient>(), Auth);

        await reloadedStore.InitialiseAsync();

        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/workspace" }));
        Assert.That(restored!.Workspace.DisplayName, Is.EqualTo("Second offline edit"));
        Assert.That(reloadedStore.SyncState, Is.EqualTo(WorkspaceSyncState.Saved));
    }

    [Test]
    public async Task ExpiredCoachRequestIsSavedLocallyWithoutShowingTheSignedOutDemoReply()
    {
        await SignInAsync();
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(new ApiError("unauthorized", "The access token expired."), options: SharedCommon.JsonOptions)
        });

        await Coach.SendMessage(new() { Content = "What should I focus on?", UserId = "user" });

        Assert.That(Auth.IsSessionExpired, Is.True);
        Assert.That(Store.Data.Conversations[0].Messages.Last().Text, Does.Contain("Your session expired"));
        Assert.That(Store.Data.Conversations[0].Messages.Last().Text, Does.Not.Contain("feels like a useful next step"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.AuthenticationExpired));
    }

    [Test]
    public async Task ExpiredCheckoutRequiresReauthenticationWithoutLeavingTheCurrentPage()
    {
        await SignInAsync();
        var currentUrl = Navigation.Uri;
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = JsonContent.Create(new ApiError("unauthorized", "The access token expired."), options: SharedCommon.JsonOptions)
        });

        await Billing.BuyPro();

        Assert.That(Auth.IsSessionExpired, Is.True);
        Assert.That(Billing.BillingMessage, Does.Contain("session expired"));
        Assert.That(Navigation.Uri, Is.EqualTo(currentUrl));
    }

    [Test]
    public async Task DisposedCardsStopRenderingWhileLiveCardsStillUpdate()
    {
        var disposed = Render<FocusTimeCard>();
        var live = Render<FocusTimeCard>();
        disposed.Dispose();
        var renderCount = disposed.RenderCount;
        AddSession(25);
        await live.InvokeAsync(() => Stats.Period = ReportPeriod.Day);
        await live.InvokeAsync(Store.SaveAsync);
        live.WaitForState(() => live.FindAll("strong").Any(element => Content(element) == "25 min"));
        Assert.That(Content(live.Find("strong")), Is.EqualTo("25 min"));
        Assert.That(disposed.RenderCount, Is.EqualTo(renderCount));
    }

    [Test]
    public void FocusTimerCardWithAnEmptyPlanNavigatesToTasksWhenChangingTask()
    {
        Store.Data.Tasks.Clear();
        var cut = Render<FocusTimerCard>();
        cut.Find(".session-note button").Click();
        Assert.That(Navigation.Uri, Does.EndWith("/tasks"));
    }

    [Test]
    public void PricingPageRetainsTheBillingIntervalWhenRecreated()
    {
        var first = Render<PricingPage>();
        first.FindAll(".billing-toggle button")[1].Click();
        first.Dispose();
        var second = Render<PricingPage>();
        Assert.That(second.FindAll(".billing-toggle button")[1].ClassName, Does.Contain("selected"));
        Assert.That(second.Find(".checkout-button").TextContent, Does.Contain("Subscribe annually"));
    }

    [Test]
    public void RoutesPreserveSettingsHashAndQueryWhileSelectingTheCorrectTitle()
    {
        PreserveWorkspaceForInitialization();
        Navigation.NavigateTo("settings?source=profile#billing");
        var cut = Render<Routes>();
        cut.WaitForState(() => cut.FindAll(".crumb strong").Any(element => Content(element) == "Settings"));
        Assert.That(Content(cut.Find(".crumb strong")), Is.EqualTo("Settings"));
        Assert.That(cut.FindAll("#billing"), Has.Exactly(1).Items);
    }

    private static JsonElement Property(JsonElement element, string name) => element.EnumerateObject()
        .Single(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
