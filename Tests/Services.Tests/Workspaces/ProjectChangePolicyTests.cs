using Services.Workspaces;
using Shared.Enums;
using Shared.Models;

namespace Services.Tests.Workspaces;

public sealed class ProjectChangePolicyTests
{
    [TestCase(BillingPlan.Free, 3)]
    [TestCase(BillingPlan.Pro, 25)]
    public void CanAddOrRestore_ActiveProjectLimit_RejectsExcessProjects(BillingPlan plan, int limit)
    {
        var projects = Enumerable.Range(0, limit).Select(index => new ProjectRecord
        {
            Id = index.ToString()
        }).ToList();

        Assert.That(ProjectChangePolicy.CanAddOrRestore(projects, plan), Is.EqualTo(ProjectChangeRejection.ProjectLimit));
        projects[0].IsArchived = true;
        Assert.That(ProjectChangePolicy.CanAddOrRestore(projects, plan), Is.EqualTo(ProjectChangeRejection.None));
    }

    [TestCase(TimerPhase.Focus)]
    [TestCase(TimerPhase.ShortBreak)]
    [TestCase(TimerPhase.LongBreak)]
    [TestCase(TimerPhase.Paused)]
    public void CanArchive_ActiveTimerReferencingTask_RejectsArchive(TimerPhase phase)
    {
        var project = new ProjectRecord
        {
            Id = "project"
        };
        var timer = new TimerSnapshot
        {
            Phase = phase,
            TaskId = "task"
        };
        var task = new TaskRecord
        {
            Id = "task",
            ProjectId = project.Id
        };

        Assert.That(ProjectChangePolicy.CanArchive(project, timer, [task]), Is.EqualTo(ProjectChangeRejection.TimerActive));
        timer.Phase = TimerPhase.Idle;
        Assert.That(ProjectChangePolicy.CanArchive(project, timer, [task]), Is.EqualTo(ProjectChangeRejection.None));
    }

    [Test]
    public void CanArchive_DirectTimerAndProjectState_ReturnsExpectedDecision()
    {
        var timer = new TimerSnapshot
        {
            Phase = TimerPhase.Focus,
            ProjectId = "project"
        };
        var project = new ProjectRecord
        {
            Id = "project"
        };

        Assert.That(ProjectChangePolicy.CanArchive(null, timer, []), Is.EqualTo(ProjectChangeRejection.NotFound));
        Assert.That(ProjectChangePolicy.CanArchive(project, timer, []), Is.EqualTo(ProjectChangeRejection.TimerActive));
        project.IsArchived = true;
        Assert.That(ProjectChangePolicy.CanArchive(project, timer, []), Is.EqualTo(ProjectChangeRejection.AlreadyArchived));
    }
}
