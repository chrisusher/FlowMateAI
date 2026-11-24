using System.Net;
using System.Net.Http.Json;
using ChrisUsher.Core.Shared;
using Shared.Contracts;
using Shared.Enums;
using Web.Components.Features.Editors;
using Web.Components.Features.Projects;
using Web.Components.Features.Tasks;
using Web.Components.Pages;

namespace Web.Tests;

public sealed class TaskAndProjectComponentTests : WorkspaceComponentTest
{
    [Test]
    public void ProjectTaskStartsFocusAndOpensToday()
    {
        var task = Store.Data.Tasks[1];
        Navigation.NavigateTo("projects/project-one");
        var cut = Render<AllTasksCard>(p => p.Add(c => c.ProjectId, Project.Id));

        cut.Find($"button[aria-label='Start focus on {task.Title}']").Click();

        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.Data.Timer.TaskId, Is.EqualTo(task.Id));
        Assert.That(Store.Data.Timer.ProjectId, Is.EqualTo(Project.Id));
        Assert.That(Store.Data.Timer.DurationSeconds, Is.EqualTo(1500));
        Assert.That(Navigation.Uri, Does.EndWith("/"));
    }

    [TestCase(TimerPhase.ShortBreak)]
    [TestCase(TimerPhase.LongBreak)]
    public async Task ExistingTimerCannotBeReassignedByTaskActions(TimerPhase phase)
    {
        Store.Data.Timer.Phase = phase;
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        var otherTask = Store.Data.Tasks[1];
        var cut = Render<AllTasksCard>(p => p.Add(c => c.ProjectId, Project.Id));

        Assert.That(cut.Find($"button[aria-label='Start focus on {otherTask.Title}']").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find(".task-focus-notice").TextContent, Does.Contain("already in progress"));
        Assert.That(await Timer.StartFocusForTaskAsync(otherTask.Id), Is.False);
        await Tasks.SelectTask(otherTask);
        Assert.That(Store.Data.Timer.TaskId, Is.EqualTo(PlannedTask.Id));
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(phase));
    }

    [TestCase(TimerPhase.Focus)]
    [TestCase(TimerPhase.Paused)]
    public async Task StartingAnotherTaskPreservesRemainingTimeAndRecordsPreviousWork(TimerPhase phase)
    {
        var timer = Store.Data.Timer;
        var now = Store.UtcNow;
        timer.Phase = phase;
        timer.TaskId = PlannedTask.Id;
        timer.ProjectId = Project.Id;
        timer.DurationSeconds = 1500;
        timer.RemainingSeconds = 900;
        timer.FocusSessionId = "first-session";
        timer.CompletedIntervals.Add(new() { StartedAt = now.AddMinutes(-12), EndedAt = now.AddMinutes(-10) });
        timer.StartedAt = phase == TimerPhase.Focus ? now.AddMinutes(-8) : null;
        timer.EndsAt = phase == TimerPhase.Focus ? now.AddMinutes(15) : null;
        var deadline = timer.EndsAt;
        var otherTask = Store.Data.Tasks[1];
        var cut = Render<AllTasksCard>();

        Assert.That(cut.Find($"button[aria-label='Start focus on {otherTask.Title}']").HasAttribute("disabled"), Is.False);
        Assert.That(await Timer.StartFocusForTaskAsync(otherTask.Id), Is.True);
        Assert.That(timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(timer.TaskId, Is.EqualTo(otherTask.Id));
        Assert.That(timer.EndsAt, phase == TimerPhase.Focus ? Is.EqualTo(deadline) : Is.InRange(now.AddSeconds(900), Store.UtcNow.AddSeconds(900)));
        Assert.That(timer.CompletedIntervals, Is.Empty);
        Assert.That(Store.Data.Sessions.Single().TaskId, Is.EqualTo(PlannedTask.Id));
        Assert.That(Store.Data.Sessions.Single().FocusMinutes, Is.EqualTo(phase == TimerPhase.Focus ? 10 : 2));
        Assert.That(FocusSessionRecord.TotalSessions(Store.Data.Sessions), Is.EqualTo(phase == TimerPhase.Focus ? 0.4 : 0.08).Within(0.001));
        Assert.That(timer.FocusSessionId, Is.Not.EqualTo("first-session"));
    }

    [Test]
    public async Task AnotherClientCannotSwitchAnActiveTimer()
    {
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.OwnerClientId = "another-client";
        var otherTask = Store.Data.Tasks[1];
        var cut = Render<AllTasksCard>();

        Assert.That(cut.Find($"button[aria-label='Start focus on {otherTask.Title}']").HasAttribute("disabled"), Is.True);
        Assert.That(await Timer.StartFocusForTaskAsync(otherTask.Id), Is.False);
        Assert.That(Store.Data.Timer.TaskId, Is.EqualTo(PlannedTask.Id));
    }

    [Test]
    public async Task SwitchingTasksPreservesFractionalMinutesInFocusTotals()
    {
        Store.Data.TimeZone = Store.UtcNow.TimeOfDay < TimeSpan.FromMinutes(15) ? "Pacific/Honolulu" : "UTC";
        await Timer.StartFocusForTaskAsync(PlannedTask.Id);
        var timer = Store.Data.Timer;
        timer.StartedAt = Store.UtcNow.AddSeconds(-750);
        timer.EndsAt = Store.UtcNow.AddSeconds(750);
        var otherTask = Store.Data.Tasks[1];

        Assert.That(await Timer.StartFocusForTaskAsync(otherTask.Id), Is.True);
        timer.StartedAt = Store.UtcNow.AddSeconds(-750);
        await Timer.EndFocus();

        Assert.That(Store.Data.Sessions.Select(s => s.TaskId), Is.EquivalentTo(new[] { PlannedTask.Id, otherTask.Id }));
        Assert.That(Stats.TodayMinutes, Is.EqualTo(25));
        Assert.That(Stats.PeriodMinutes, Is.EqualTo(25));
        Assert.That(Stats.TodaySessions, Is.EqualTo(1).Within(0.001));
        Assert.That(Stats.PeriodSessions, Is.EqualTo(1).Within(0.001));
    }

    [Test]
    public async Task FocusCannotStartForCompletedOrArchivedTasks()
    {
        PlannedTask.IsComplete = true;
        Assert.That(await Timer.StartFocusForTaskAsync(PlannedTask.Id), Is.False);
        PlannedTask.IsComplete = false;
        Project.IsArchived = true;
        Assert.That(await Timer.StartFocusForTaskAsync(PlannedTask.Id), Is.False);
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Idle));
    }

    [Test]
    public void AllTasksCardSortsAndHandlesTaskActions()
    {
        var cut = Render<AllTasksCard>();
        Assert.That(cut.FindAll(".task-title").Select(Content), Is.EqualTo(new[] { "Choose one thing", "Take a reset" }));
        cut.Find(".check-circle").Click();
        Assert.That(PlannedTask.IsComplete, Is.True);
        Assert.That(Content(cut.Find(".task-title")), Is.EqualTo("Take a reset"));
        cut.Find(".plan-toggle").Click();
        Assert.That(Store.Data.Tasks[1].PlannedToday, Is.True);
        cut.Find(".task-edit").Click();
        Assert.That(Tasks.Editing, Is.True);
        Assert.That(Tasks.DialogOpen, Is.True);
        Assert.That(Tasks.Draft.Title, Is.EqualTo("Take a reset"));
        Tasks.CloseTaskDialog();
        cut.Find(".filter-chip").Click();
        cut.FindAll(".check-circle")[1].Click();
        Assert.That(PlannedTask.IsComplete, Is.False);
    }

    [Test]
    public void AllTasksFilterTogglesCompletedTasksAndUpdatesWhenReopened()
    {
        PlannedTask.IsComplete = true;
        var cut = Render<AllTasksCard>();

        Assert.That(cut.FindAll(".task-title").Select(Content), Does.Not.Contain(PlannedTask.Title));
        Assert.That(cut.Find(".filter-chip").GetAttribute("aria-pressed"), Is.EqualTo("false"));
        cut.Find(".filter-chip").Click();
        Assert.That(cut.Find(".filter-chip").GetAttribute("aria-pressed"), Is.EqualTo("true"));
        Assert.That(cut.FindAll(".task-title").Select(Content), Does.Contain(PlannedTask.Title));
        cut.FindAll(".check-circle")[1].Click();
        cut.Find(".filter-chip").Click();
        Assert.That(cut.FindAll(".task-title").Select(Content), Does.Contain(PlannedTask.Title));
    }

    [Test]
    public void AllTasksCardTodayControlTogglesEachTaskAndSelectsActiveTask()
    {
        var cut = Render<AllTasksCard>();
        cut.FindAll(".filter-chip")[1].Click();
        Assert.That(PlannedTask.PlannedToday, Is.False);
        Assert.That(Store.Data.Tasks[1].PlannedToday, Is.True);
        cut.Find(".task-title").Click();
        Assert.That(Store.Data.Timer.TaskId, Is.EqualTo(PlannedTask.Id));
    }

    [Test]
    public void TaskEditorDialogCreatesAndEditsWithoutMutatingTheOriginalDraft()
    {
        var cut = Render<TaskEditorDialog>();
        Assert.That(cut.FindAll("[role=dialog]"), Is.Empty);
        Tasks.AddQuickTask();
        cut.WaitForElement("[role=dialog]");
        cut.Find("input.field-input").Change("New task");
        cut.Find("form").Submit();
        Assert.That(Store.Data.Tasks.Count, Is.EqualTo(3));
        Assert.That(Store.Data.Tasks[2].Title, Is.EqualTo("New task"));
        Assert.That(cut.FindAll("[role=dialog]"), Is.Empty);
        Tasks.OpenTaskEditor(PlannedTask);
        cut.WaitForElement("input.field-input").Change("Edited task");
        Assert.That(PlannedTask.Title, Is.EqualTo("Choose one thing"));
        cut.Find("form").Submit();
        Assert.That(PlannedTask.Title, Is.EqualTo("Edited task"));
        Assert.That(Store.Data.Tasks.Count, Is.EqualTo(3));
    }

    [Test]
    public void TaskEditorDialogRejectsBlankNamesAndCancelLeavesTasksUnchanged()
    {
        Tasks.AddQuickTask();
        var cut = Render<TaskEditorDialog>();
        cut.Find("form").Submit();
        Assert.That(Store.Data.Tasks.Count, Is.EqualTo(2));
        Assert.That(cut.FindAll("[role=dialog]"), Has.Exactly(1).Items);
        cut.Find(".dialog-close").Click();
        Assert.That(Tasks.DialogOpen, Is.False);
        Assert.That(cut.FindAll("[role=dialog]"), Is.Empty);
    }

    [Test]
    public void TaskEditorDialogValidatesLongAndUnsafeNamesAndClosesOnEscape()
    {
        Tasks.AddQuickTask();
        var cut = Render<TaskEditorDialog>();
        cut.Find("input.field-input").Change(new string('x', 161));
        cut.Find("form").Submit();

        Assert.That(Tasks.ValidationMessage, Is.EqualTo("Task names can be up to 160 characters."));
        Assert.That(Store.Data.Tasks.Count, Is.EqualTo(2));

        cut.Find("input.field-input").Change("Safe name\nwith control");
        cut.Find("form").Submit();
        Assert.That(Tasks.ValidationMessage, Is.EqualTo("Task names cannot contain control characters."));

        cut.Find("[role=dialog]").KeyDown("Escape");
        Assert.That(Tasks.DialogOpen, Is.False);
    }

    [Test]
    public void MovingTaskPreservesFocusHistoryAndUpdatesIdleTimerSelection()
    {
        var destination = new ProjectRecord { Id = "project-two", Name = "Client work" };
        Store.Data.Projects.Add(destination);
        AddSession(25);
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        var cut = Render<TaskEditorDialog>();

        Tasks.OpenTaskEditor(PlannedTask);
        cut.Find("select.field-input").Change(destination.Id);
        cut.Find("form").Submit();

        Assert.That(PlannedTask.ProjectId, Is.EqualTo(destination.Id));
        Assert.That(Store.Data.Timer.ProjectId, Is.EqualTo(destination.Id));
        Assert.That(Store.Data.Sessions.Single().TaskId, Is.EqualTo(PlannedTask.Id));
        Assert.That(Store.Data.Sessions.Single().ProjectId, Is.EqualTo(Project.Id));
    }

    [Test]
    public void RemovingTaskRequiresConfirmationAndKeepsFocusHistory()
    {
        var removedTask = PlannedTask;
        AddSession(25);
        Store.Data.Timer.TaskId = removedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        var cut = Render<AllTasksCard>();

        cut.Find(".task-remove").Click();
        cut.WaitForElement("[role=alertdialog]");
        Assert.That(Store.Data.Tasks, Does.Contain(removedTask));
        cut.Find("[role=alertdialog] .dialog-actions .timer-primary").Click();

        Assert.That(Store.Data.Tasks, Does.Not.Contain(removedTask));
        Assert.That(Store.Data.Sessions.Single().TaskId, Is.EqualTo(removedTask.Id));
        Assert.That(Store.Data.Sessions.Single().ProjectId, Is.EqualTo(Project.Id));
        Assert.That(Store.Data.Timer.TaskId, Is.Null);
        Assert.That(Store.Data.Timer.ProjectId, Is.Empty);
    }

    [Test]
    public void ActiveTaskCannotBeMovedOrRemovedUntilTimerIsStopped()
    {
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Projects.Add(new()
        {
            Id = "project-two",
            Name = "Client work"
        });
        var editor = Render<TaskEditorDialog>();

        Tasks.OpenTaskEditor(PlannedTask);
        editor.Find("select.field-input").Change("project-two");
        editor.Find("form").Submit();
        Assert.That(PlannedTask.ProjectId, Is.EqualTo(Project.Id));
        Assert.That(Tasks.ValidationMessage, Does.Contain("Pause or finish"));

        Tasks.CloseTaskDialog();
        var tasks = Render<AllTasksCard>();
        tasks.Find(".task-remove").Click();
        Assert.That(Tasks.RemoveTaskId, Is.Null);
        Assert.That(Tasks.ActionMessage, Does.Contain("Pause or finish"));
        Assert.That(Store.Data.Tasks, Does.Contain(PlannedTask));
    }

    [Test]
    public void TaskNamesRenderAsTextAndRemovalCanBeCancelled()
    {
        PlannedTask.Title = "<img src=x onerror=alert(1)>";
        var cut = Render<AllTasksCard>();

        Assert.That(cut.FindAll("img"), Is.Empty);
        Assert.That(cut.Find(".task-title").TextContent, Is.EqualTo(PlannedTask.Title));
        cut.Find(".task-remove").Click();
        cut.Find("[role=alertdialog] .text-button").Click();

        Assert.That(Store.Data.Tasks, Does.Contain(PlannedTask));
        Assert.That(cut.FindAll("[role=alertdialog]"), Is.Empty);
    }

    [Test]
    public void TasksWithArchivedOrRemovedProjectsCanBeMovedToAnActiveProject()
    {
        var destination = new ProjectRecord { Id = "project-two", Name = "Client work" };
        Store.Data.Projects.Add(destination);
        Project.IsArchived = true;
        var cut = Render<TaskEditorDialog>();

        Tasks.OpenTaskEditor(PlannedTask);
        cut.Find("select.field-input").Change(destination.Id);
        cut.Find("form").Submit();
        Assert.That(PlannedTask.ProjectId, Is.EqualTo(destination.Id));

        PlannedTask.ProjectId = "deleted-project";
        Tasks.OpenTaskEditor(PlannedTask);
        cut.Find("select.field-input").Change(destination.Id);
        cut.Find("form").Submit();
        Assert.That(PlannedTask.ProjectId, Is.EqualTo(destination.Id));
    }

    [Test]
    public void TaskInArchivedProjectCannotBeSelectedForFocus()
    {
        Project.IsArchived = true;

        var cut = Render<AllTasksCard>();
        cut.Find(".task-title").Click();

        Assert.That(Store.Data.Timer.TaskId, Is.Null);
        Assert.That(Tasks.ActionMessage, Does.Contain("active project"));
    }

    [Test]
    public void AllTasksCardShowsAnEmptyWorkspaceMessage()
    {
        Store.Data.Tasks.Clear();
        var cut = Render<AllTasksCard>();

        Assert.That(cut.Find(".empty-state").TextContent, Does.Contain("No tasks yet"));
    }

    [Test]
    public void ProjectCardShowsProgressAndFocusForItsParameter()
    {
        PlannedTask.IsComplete = true;
        AddSession(25);
        var cut = Render<ProjectCard>(p => p.Add(c => c.Project, Project));
        Assert.That(Content(cut.Find("h3")), Is.EqualTo("Personal"));
        Assert.That(cut.Find("p").TextContent, Does.Contain("1 active tasks"));
        Assert.That(Content(cut.Find(".project-progress small")), Is.EqualTo("1/2"));
        Assert.That(cut.Find(".project-progress i").GetAttribute("style"), Does.Contain("50%"));
        Assert.That(Content(cut.Find(".project-card-foot")), Does.Contain("25 min focused"));
    }

    [Test]
    public void ProjectCardHandlesAnEmptyProject()
    {
        var cut = Render<ProjectCard>(p => p.Add(c => c.Project, new ProjectRecord { Name = "Empty" }));
        Assert.That(Content(cut.Find(".project-progress small")), Is.EqualTo("0/0"));
        Assert.That(cut.Find(".project-progress i").GetAttribute("style"), Does.Contain("0%"));
    }

    [Test]
    public void ProjectCardCreatesATaskForTheSelectedProject()
    {
        var other = new ProjectRecord
        {
            Id = "project-two",
            Name = "Client work"
        };
        Store.Data.Projects.Add(other);
        var card = Render<ProjectCard>(p => p.Add(c => c.Project, other));
        var editor = Render<TaskEditorDialog>();

        Assert.That(card.Find(".project-card-link").GetAttribute("href"), Is.EqualTo("projects/project-two"));
        card.Find(".project-add-task").Click();
        editor.WaitForElement("[role=dialog]");
        Assert.That(Tasks.Draft.ProjectId, Is.EqualTo(other.Id));
        editor.Find("input.field-input").Change("Client task");
        editor.Find("form").Submit();

        Assert.That(Store.Data.Tasks.Single(task => task.Title == "Client task").ProjectId, Is.EqualTo(other.Id));
    }

    [Test]
    public void ProjectDetailShowsOnlyItsTasksAndAllowsAddingAnother()
    {
        var other = new ProjectRecord
        {
            Id = "project-two",
            Name = "Client work"
        };
        Store.Data.Projects.Add(other);
        Store.Data.Tasks.Add(new()
        {
            ProjectId = other.Id,
            Title = "Client task",
            IsComplete = true
        });

        var cut = Render<ProjectDetailPage>(p => p.Add(c => c.ProjectId, other.Id));

        Assert.That(Content(cut.Find("h1")), Is.EqualTo("Client work"));
        Assert.That(cut.FindAll(".task-title"), Is.Empty);
        Assert.That(cut.Find(".empty-state").TextContent, Does.Contain("No open tasks"));
        cut.Find(".filter-chip").Click();
        Assert.That(cut.FindAll(".task-title").Select(Content), Is.EqualTo(new[] { "Client task" }));
        cut.Find(".filter-chip").Click();
        Assert.That(cut.FindAll(".task-title"), Is.Empty);
        Assert.That(cut.FindAll(".filter-chip"), Has.Exactly(1).Items);
        cut.Find(".page-heading button").Click();
        Assert.That(Tasks.Draft.ProjectId, Is.EqualTo(other.Id));
        Assert.That(Tasks.DialogOpen, Is.True);
    }

    [Test]
    public void ProjectDetailHandlesMissingAndArchivedProjects()
    {
        var missing = Render<ProjectDetailPage>(p => p.Add(c => c.ProjectId, "missing"));
        Assert.That(Content(missing.Find("h1")), Is.EqualTo("Project not found"));

        Project.IsArchived = true;
        var archived = Render<ProjectDetailPage>(p => p.Add(c => c.ProjectId, Project.Id));
        Assert.That(archived.FindAll(".page-heading button"), Is.Empty);
        Assert.That(archived.FindAll(".task-title"), Has.Exactly(2).Items);
        Assert.That(Content(archived.Find(".subheading")), Does.Contain("archived"));
    }

    [Test]
    public void ProjectTipCardLinksToPricing()
    {
        var cut = Render<ProjectTipCard>();
        Assert.That(cut.Find("p").TextContent, Does.Contain("up to 3 projects"));
        Assert.That(cut.Find("a").GetAttribute("href"), Is.EqualTo("pricing"));
    }

    [Test]
    public async Task ProjectEditorDialogTrimsNamesPersistsAndCancels()
    {
        await SignInAsync();
        UseProjectApi();
        var cut = Render<ProjectEditorDialog>();
        await Projects.AddProject();
        cut.WaitForElement("[role=dialog]");
        Assert.That(cut.Find("button[type=submit]").HasAttribute("disabled"), Is.True);
        cut.Find("input").Change("  Project two  ");
        await cut.InvokeAsync(Projects.SaveProject);
        Assert.That(Store.Data.Projects[1].Name, Is.EqualTo("Project two"));
        Assert.That(cut.FindAll("[role=dialog]"), Is.Empty);
        await Projects.AddProject();
        cut.WaitForElement(".dialog-close").Click();
        Assert.That(Store.Data.Projects.Count, Is.EqualTo(2));
        Assert.That(Projects.DialogOpen, Is.False);
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/projects" }));
    }

    [Test]
    public void ProjectCardMenuOpensRenameAndArchiveActions()
    {
        var cut = Render<ProjectCard>(p => p.Add(c => c.Project, Project));

        Assert.That(cut.Find(".project-menu summary").GetAttribute("aria-label"), Is.EqualTo("Actions for Personal"));
        cut.Find(".project-menu-actions button").Click();

        Assert.That(Projects.DialogOpen, Is.True);
        Assert.That(Projects.EditingProjectId, Is.EqualTo(Project.Id));
        Assert.That(Projects.DraftName, Is.EqualTo("Personal"));
    }

    [Test]
    public async Task ProjectCanBeRenamedWithoutChangingTasksOrFocusHistory()
    {
        await SignInAsync();
        UseProjectApi();
        AddSession(20);
        var cut = Render<ProjectEditorDialog>();
        Projects.EditProject(Project);
        cut.WaitForElement("input").Change("  Client work  ");
        await cut.InvokeAsync(Projects.SaveProject);

        Assert.That(Project.Name, Is.EqualTo("Client work"));
        Assert.That(Store.Data.Tasks.Select(task => task.ProjectId), Is.All.EqualTo(Project.Id));
        Assert.That(Store.Data.Sessions.Single().ProjectId, Is.EqualTo(Project.Id));
        Assert.That(cut.FindAll("[role=dialog]"), Is.Empty);
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/projects/project-one", "/api/v1/projects/project-one" }));
    }

    [Test]
    public async Task ProjectCreateWaitsForTheApiAndReusesItsIdAfterFailure()
    {
        await SignInAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new List<ProjectCreateRequest>();
        Api.Respond = async (request, cancellationToken) =>
        {
            var input = (await request.Content!.ReadFromJsonAsync<ProjectCreateRequest>(SharedCommon.JsonOptions, cancellationToken))!;
            attempts.Add(input);

            if (attempts.Count == 1)
            {
                started.SetResult();
                await finish.Task.WaitAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = JsonContent.Create(new ApiError("unavailable", "Try again."), options: SharedCommon.JsonOptions)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new ProjectWriteResponse(new ProjectRecord
                {
                    Id = input.Id!,
                    Name = input.Name!,
                    Color = input.Color!,
                    CreatedAt = input.CreatedAt!.Value
                }, "revision"), options: SharedCommon.JsonOptions)
            };
        };
        await Projects.AddProject();
        Projects.DraftName = "New project";
        var saving = Projects.SaveProject();
        await started.Task;
        Assert.That(Store.Data.Projects.Count, Is.EqualTo(1));
        Assert.That(Projects.Saving, Is.True);
        finish.SetResult();
        await saving;
        Assert.That(Projects.DialogOpen, Is.True);
        Assert.That(Projects.ValidationMessage, Is.EqualTo("Try again."));
        Assert.That(Store.Data.Projects.Count, Is.EqualTo(1));

        await Projects.SaveProject();
        Assert.That(Store.Data.Projects.Count, Is.EqualTo(2));
        Assert.That(attempts[1].Id, Is.EqualTo(attempts[0].Id));
        Assert.That(attempts[1].CreatedAt, Is.EqualTo(attempts[0].CreatedAt));
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/projects", "/api/v1/projects" }));
    }

    [Test]
    public async Task RenameKeepsDraftWhenServerProjectChanged()
    {
        await SignInAsync();
        Api.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProjectRecord
                {
                    Id = Project.Id,
                    Name = "Changed elsewhere",
                    Color = Project.Color,
                    CreatedAt = Project.CreatedAt
                }, options: SharedCommon.JsonOptions)
            };
            response.Headers.TryAddWithoutValidation("ETag", "\"revision\"");

            return Task.FromResult(response);
        };
        Projects.EditProject(Project);
        Projects.DraftName = "My draft";

        await Projects.SaveProject();

        Assert.That(Projects.DialogOpen, Is.True);
        Assert.That(Projects.DraftName, Is.EqualTo("My draft"));
        Assert.That(Projects.ValidationMessage, Does.Contain("changed on another device"));
        Assert.That(Project.Name, Is.EqualTo("Personal"));
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/projects/project-one" }));
    }

    [Test]
    public async Task ProjectCanBeArchivedAndRestoredWithoutLosingRelatedRecords()
    {
        await SignInAsync();
        UseProjectApi();
        AddSession(20);
        await Store.SaveAsync();
        var cut = Render<ProjectsPage>();
        Projects.RequestArchive(Project);
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Archive Personal?")));
        await cut.InvokeAsync(Projects.ConfirmArchiveAsync);

        Assert.That(Project.IsArchived, Is.True);
        Assert.That(Store.Data.Tasks.Count(task => task.ProjectId == Project.Id), Is.EqualTo(2));
        Assert.That(Store.Data.Sessions.Single().ProjectId, Is.EqualTo(Project.Id));
        Assert.That(cut.Find(".archived-projects").TextContent, Does.Contain("20 min focused"));

        await cut.InvokeAsync(() => Projects.RestoreProjectAsync(Project));
        Assert.That(Project.IsArchived, Is.False);
        Assert.That(cut.FindAll(".archived-projects").Count, Is.Zero);
    }

    [Test]
    public void ProjectCannotBeArchivedWhileItsTimerIsActive()
    {
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Timer.ProjectId = Project.Id;

        Projects.RequestArchive(Project);

        Assert.That(Project.IsArchived, Is.False);
        Assert.That(Projects.ArchiveProjectId, Is.Null);
        Assert.That(Projects.ActionMessage, Does.Contain("Pause or finish"));
    }

    [Test]
    public async Task ArchivingSelectedProjectClearsIdleTimerSelection()
    {
        await SignInAsync();
        UseProjectApi();
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;
        await Store.SaveAsync();

        Projects.RequestArchive(Project);
        await Projects.ConfirmArchiveAsync();

        Assert.That(Store.Data.Timer.TaskId, Is.Null);
        Assert.That(Store.Data.Timer.ProjectId, Is.Empty);
    }

    [Test]
    public async Task ArchiveFailureLeavesProjectAndTimerUntouched()
    {
        await SignInAsync();
        UseProjectApi();
        Store.Data.Timer.ProjectId = Project.Id;
        Store.Data.Timer.TaskId = PlannedTask.Id;
        await Store.SaveAsync();
        Api.Paths.Clear();
        Api.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Project, options: SharedCommon.JsonOptions)
                };
                response.Headers.TryAddWithoutValidation("ETag", "\"revision\"");

                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = JsonContent.Create(new ApiError("timer_active", "Finish the timer first."), options: SharedCommon.JsonOptions)
            });
        };
        Projects.RequestArchive(Project);

        await Projects.ConfirmArchiveAsync();

        Assert.That(Project.IsArchived, Is.False);
        Assert.That(Store.Data.Timer.ProjectId, Is.EqualTo(Project.Id));
        Assert.That(Store.Data.Timer.TaskId, Is.EqualTo(PlannedTask.Id));
        Assert.That(Projects.ArchiveProjectId, Is.EqualTo(Project.Id));
        Assert.That(Projects.ActionMessage, Is.EqualTo("Finish the timer first."));
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/projects/project-one", "/api/v1/projects/project-one/archive" }));
    }

    [Test]
    public async Task ArchiveDoesNotOverwriteTimerChangesMadeWhileRequestIsInFlight()
    {
        await SignInAsync();
        UseProjectApi();
        await Store.SaveAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Api.Respond = async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                var read = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Project, options: SharedCommon.JsonOptions)
                };
                read.Headers.TryAddWithoutValidation("ETag", "\"revision\"");

                return read;
            }

            entered.SetResult();
            await finish.Task.WaitAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProjectWriteResponse(new ProjectRecord
                {
                    Id = Project.Id,
                    Name = Project.Name,
                    Color = Project.Color,
                    CreatedAt = Project.CreatedAt,
                    IsArchived = true
                }, "\"new-revision\"", "workspace-revision-2", new TimerSnapshot()), options: SharedCommon.JsonOptions)
            };
        };
        Projects.RequestArchive(Project);
        var archive = Projects.ConfirmArchiveAsync();
        await entered.Task;
        Store.Data.Timer.Phase = TimerPhase.Focus;
        Store.Data.Timer.ProjectId = Project.Id;
        finish.SetResult();
        await archive;

        Assert.That(Project.IsArchived, Is.True);
        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.SyncState, Is.EqualTo(WorkspaceSyncState.Pending));
    }

    [Test]
    public async Task StartingFocusRejectsAnArchivedProjectSelectionAndDoesNotSelectItsTasks()
    {
        Project.IsArchived = true;
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;

        await Timer.StartFocus();

        Assert.That(Store.Data.Timer.Phase, Is.EqualTo(TimerPhase.Focus));
        Assert.That(Store.Data.Timer.TaskId, Is.Null);
        Assert.That(Store.Data.Timer.ProjectId, Is.Empty);
        Assert.That(Stats.PlannedTasks, Is.Empty);
    }

    [Test]
    public async Task ArchivedProjectFreesAnActiveSlotAndInvalidNamesAreRejected()
    {
        await SignInAsync();
        UseProjectApi();
        Store.Data.Projects.AddRange(Enumerable.Range(2, 2).Select(i => new ProjectRecord { Id = $"project-{i}", Name = $"Project {i}" }));
        await Store.SaveAsync();
        Projects.RequestArchive(Project);
        await Projects.ConfirmArchiveAsync();
        await Projects.AddProject();
        Projects.DraftName = "   ";
        await Projects.SaveProject();

        Assert.That(Projects.DialogOpen, Is.True);
        Assert.That(Projects.ValidationMessage, Is.EqualTo("Enter a project name."));
        Projects.DraftName = new string('x', 81);
        await Projects.SaveProject();
        Assert.That(Projects.ValidationMessage, Is.EqualTo("Project names can be up to 80 characters."));
        Projects.DraftName = "Invalid\nname";
        await Projects.SaveProject();
        Assert.That(Projects.ValidationMessage, Is.EqualTo("Project names cannot contain control characters."));
        Projects.DraftName = "Replacement";
        await Projects.SaveProject();

        Assert.That(Projects.ActiveProjectCount, Is.EqualTo(3));
        Assert.That(Store.Data.Projects.Count, Is.EqualTo(4));
    }

    [Test]
    public void ProjectDialogClosesOnEscape()
    {
        var cut = Render<ProjectEditorDialog>();
        Projects.AddProject();
        cut.WaitForElement("[role=dialog]").KeyDown("Escape");

        Assert.That(Projects.DialogOpen, Is.False);
    }

    [TestCase(BillingPlan.Free, 3)]
    [TestCase(BillingPlan.Pro, 25)]
    public void ProjectEditorDialogRechecksThePlanLimitAtSave(BillingPlan plan, int limit)
    {
        Store.Data.Plan = plan;
        Projects.AddProject();
        var cut = Render<ProjectEditorDialog>();
        cut.Find("input").Change("One more");
        Store.Data.Projects.AddRange(Enumerable.Range(1, limit - 1).Select(i => new ProjectRecord { Name = $"Project {i}" }));
        cut.Find("form").Submit();
        Assert.That(Store.Data.Projects.Count, Is.EqualTo(limit));
        Assert.That(Projects.DialogOpen, Is.False);
        Assert.That(Navigation.Uri, Does.EndWith("/pricing"));
    }

    private void UseProjectApi()
    {
        Api.Respond = async (request, cancellationToken) =>
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/v1/workspace")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new WorkspaceResponse(Store.Data, "workspace-revision"), options: SharedCommon.JsonOptions)
                };
            }

            if (request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(Project, options: SharedCommon.JsonOptions)
                };
                response.Headers.TryAddWithoutValidation("ETag", "\"project-revision\"");

                return response;
            }

            ProjectRecord saved;

            if (path.EndsWith("/archive", StringComparison.Ordinal))
            {
                var timer = System.Text.Json.JsonSerializer.Deserialize<TimerSnapshot>(
                    System.Text.Json.JsonSerializer.Serialize(Store.Data.Timer, SharedCommon.JsonOptions), SharedCommon.JsonOptions)!;

                if (timer.Phase == TimerPhase.Idle && timer.ProjectId == Project.Id)
                {
                    timer.TaskId = null;
                    timer.ProjectId = "";
                }

                saved = new ProjectRecord
                {
                    Id = Project.Id,
                    Name = Project.Name,
                    Color = Project.Color,
                    CreatedAt = Project.CreatedAt,
                    IsArchived = true
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new ProjectWriteResponse(saved, "\"new-revision\"", "workspace-revision-2", timer), options: SharedCommon.JsonOptions)
                };
            }
            else if (path.EndsWith("/restore", StringComparison.Ordinal))
            {
                saved = new ProjectRecord
                {
                    Id = Project.Id,
                    Name = Project.Name,
                    Color = Project.Color,
                    CreatedAt = Project.CreatedAt,
                    IsArchived = false
                };
            }
            else if (request.Method == HttpMethod.Post)
            {
                var input = await request.Content!.ReadFromJsonAsync<ProjectCreateRequest>(SharedCommon.JsonOptions, cancellationToken);
                saved = new ProjectRecord
                {
                    Id = input!.Id!,
                    Name = input.Name!,
                    Color = input.Color!,
                    CreatedAt = input.CreatedAt!.Value
                };
            }
            else
            {
                var input = await request.Content!.ReadFromJsonAsync<ProjectRenameRequest>(SharedCommon.JsonOptions, cancellationToken);
                saved = new ProjectRecord
                {
                    Id = Project.Id,
                    Name = input!.Name!,
                    Color = Project.Color,
                    CreatedAt = Project.CreatedAt
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProjectWriteResponse(saved, "\"new-revision\""), options: SharedCommon.JsonOptions)
            };
        };
    }
}
