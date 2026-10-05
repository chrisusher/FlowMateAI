using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChrisUsher.Core.Shared;
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
        PreserveWorkspaceForInitialization();
        var cut = Render<WorkspaceLayout>();
        cut.WaitForState(() => Store.Data.Timer.Phase == TimerPhase.Focus, TimeSpan.FromSeconds(4));
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.Data.Timer.DurationSeconds, Is.EqualTo(1500));
        Assert.That(Store.Data.Sessions, Is.Empty);
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
        Assert.That(Auth.Session.SessionExpired, Is.True);
        Assert.That(Store.Data.DisplayName, Is.EqualTo("Draft kept through reauthentication"));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.AuthenticationExpired));
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/workspace" }));
        Assert.That(JSInterop.Invocations.Any(call =>
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user.pending") && Equals(call.Arguments[1], "true")), Is.True);
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
            call.Identifier == "write" && Equals(call.Arguments[0], "flowmate.workspace.v1.test-user.pending") && Equals(call.Arguments[1], "true")), Is.True);

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
    public async Task SignedOutWorkspaceIsReportedAsPendingRatherThanOffline()
    {
        await Store.SaveAsync();

        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Pending));
        Assert.That(Store.SyncMessage, Does.Contain("Saved on this device"));
        Assert.That(Api.Paths, Is.Empty);
    }

    [Test]
    public async Task KeycloakRefreshFailureIsRejectedBeforeAnAuthenticatedRequestIsSent()
    {
        Configuration["Keycloak:Url"] = "auth.example.test";
        Configuration["Keycloak:ClientId"] = "test-client";
        Configuration["Keycloak:Realm"] = "test-api";
        AuthModule.Setup<AuthSession>("initialize", _ => true).SetResult(new()
        {
            Configured = true,
            SignedIn = true,
            Sub = "test-user"
        });
        AuthModule.Setup<string?>("getAccessToken", _ => true).SetResult(null);
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
            .SetResult("true");
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
}
