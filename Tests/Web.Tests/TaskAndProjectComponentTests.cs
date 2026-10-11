using Shared.Enums;
using Web.Components.Features.Editors;
using Web.Components.Features.Projects;
using Web.Components.Features.Tasks;
using Web.Components.Pages;

namespace Web.Tests;

public sealed class TaskAndProjectComponentTests : WorkspaceComponentTest
{
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
    public void ProjectTipCardLinksToPricing()
    {
        var cut = Render<ProjectTipCard>();
        Assert.That(cut.Find("p").TextContent, Does.Contain("up to 3 projects"));
        Assert.That(cut.Find("a").GetAttribute("href"), Is.EqualTo("pricing"));
    }

    [Test]
    public void ProjectEditorDialogTrimsNamesPersistsAndCancels()
    {
        var cut = Render<ProjectEditorDialog>();
        Projects.AddProject();
        cut.WaitForElement("[role=dialog]");
        Assert.That(cut.Find("button[type=submit]").HasAttribute("disabled"), Is.True);
        cut.Find("input").Change("  Project two  ");
        cut.Find("form").Submit();
        Assert.That(Store.Data.Projects[1].Name, Is.EqualTo("Project two"));
        Assert.That(cut.FindAll("[role=dialog]"), Is.Empty);
        Projects.AddProject();
        cut.WaitForElement(".dialog-close").Click();
        Assert.That(Store.Data.Projects.Count, Is.EqualTo(2));
        Assert.That(Projects.DialogOpen, Is.False);
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
        AddSession(20);
        var cut = Render<ProjectEditorDialog>();
        Projects.EditProject(Project);
        cut.WaitForElement("input").Change("  Client work  ");
        cut.Find("form").Submit();

        Assert.That(Project.Name, Is.EqualTo("Client work"));
        Assert.That(Store.Data.Tasks.Select(task => task.ProjectId), Is.All.EqualTo(Project.Id));
        Assert.That(Store.Data.Sessions.Single().ProjectId, Is.EqualTo(Project.Id));
        Assert.That(cut.FindAll("[role=dialog]"), Is.Empty);
    }

    [Test]
    public async Task ProjectCanBeArchivedAndRestoredWithoutLosingRelatedRecords()
    {
        AddSession(20);
        var cut = Render<ProjectsPage>();
        Projects.RequestArchive(Project);
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Archive Personal?")));
        cut.Find(".dialog-actions .timer-primary").Click();

        Assert.That(Project.IsArchived, Is.True);
        Assert.That(Store.Data.Tasks.Count(task => task.ProjectId == Project.Id), Is.EqualTo(2));
        Assert.That(Store.Data.Sessions.Single().ProjectId, Is.EqualTo(Project.Id));
        Assert.That(cut.Find(".archived-projects").TextContent, Does.Contain("20 min focused"));

        cut.Find(".archived-projects button").Click();
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
        Store.Data.Timer.TaskId = PlannedTask.Id;
        Store.Data.Timer.ProjectId = Project.Id;

        Projects.RequestArchive(Project);
        await Projects.ConfirmArchiveAsync();

        Assert.That(Store.Data.Timer.TaskId, Is.Null);
        Assert.That(Store.Data.Timer.ProjectId, Is.Empty);
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
        Store.Data.Projects.AddRange(Enumerable.Range(2, 2).Select(i => new ProjectRecord { Id = $"project-{i}", Name = $"Project {i}" }));
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
}
