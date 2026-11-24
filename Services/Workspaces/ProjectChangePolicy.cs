using Shared.Enums;
using Shared.Models;

namespace Services.Workspaces;

/// <summary>Checks project invariants against the current authoritative state.</summary>
public static class ProjectChangePolicy
{
    public static ProjectChangeRejection CanAddOrRestore(IReadOnlyCollection<ProjectRecord> projects, BillingPlan plan)
    {
        var limit = plan == BillingPlan.Pro ? 25 : 3;

        return projects.Count(project => !project.IsArchived) >= limit
            ? ProjectChangeRejection.ProjectLimit
            : ProjectChangeRejection.None;
    }

    public static ProjectChangeRejection CanArchive(ProjectRecord? project, TimerSnapshot timer, IEnumerable<TaskRecord> tasks)
    {
        if (project is null)
        {
            return ProjectChangeRejection.NotFound;
        }

        if (project.IsArchived)
        {
            return ProjectChangeRejection.AlreadyArchived;
        }

        var referencesProject = timer.ProjectId == project.Id ||
            tasks.Any(task => task.Id == timer.TaskId && task.ProjectId == project.Id);

        return referencesProject && timer.Phase is (TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused)
            ? ProjectChangeRejection.TimerActive
            : ProjectChangeRejection.None;
    }
}
