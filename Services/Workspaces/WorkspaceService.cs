using ChrisUsher.Core.Shared;
using Services.Database;
using Services.Repositories;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace Services.Workspaces;

public sealed class WorkspaceService(IWorkspaceRepository repository, IActivityArchiveRepository activityArchive, IBillingRepository billingRepository,
    IProjectArchiveRepository? projectArchives = null) : IWorkspaceService
{
    public async Task<WorkspaceResponse> GetAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var document = await GetRecoveredAsync(ownerId, cancellationToken);

        var snapshot = document is null ? new WorkspaceSnapshot() : document.Workspace!;
        var entitlement = await billingRepository.GetEntitlementAsync(ownerId, cancellationToken);
        snapshot.Plan = BillingPlanExtensions.ParseOrFree(entitlement.Plan);

        return new(snapshot, document?.ETag ?? "");
    }

    public async Task<WorkspaceSaveResult> SaveAsync(string ownerId, WorkspaceSaveRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await GetRecoveredAsync(ownerId, cancellationToken);

        if (existing is not null && (string.IsNullOrEmpty(request.Revision) || !string.Equals(existing.ETag, request.Revision, StringComparison.Ordinal)))
        {
            return new(null, WorkspaceSaveErrorCode.RevisionConflict, "This workspace changed on another device. Reload it and try again.");
        }

        var current = existing is null ? new WorkspaceSnapshot() : existing.Workspace!;

        if (existing?.ProjectsAuthoritative == true &&
            WorkspaceCollectionManifest.Hash(request.Workspace.Projects) != WorkspaceCollectionManifest.Hash(current.Projects))
        {
            return new(null, WorkspaceSaveErrorCode.RevisionConflict, "Projects changed on another device. Reload before saving this workspace.");
        }

        if (existing?.ProjectsAuthoritative == true && !string.IsNullOrEmpty(request.Workspace.Timer.ProjectId) &&
            current.Projects.Any(project => project.Id == request.Workspace.Timer.ProjectId && project.IsArchived))
        {
            return new(null, WorkspaceSaveErrorCode.TimerConflict, "The selected project has been archived. Reload before changing the timer.");
        }

        var entitlement = await billingRepository.GetEntitlementAsync(ownerId, cancellationToken);
        var plan = BillingPlanExtensions.ParseOrFree(entitlement.Plan);
        var projectLimit = plan == BillingPlan.Pro ? 25 : 3;
        var existingActiveProjectIds = current.Projects
            .Where(project => !project.IsArchived)
            .Select(project => project.Id)
            .ToHashSet(StringComparer.Ordinal);
        var requestedActiveProjects = request.Workspace.Projects.Where(project => !project.IsArchived).ToArray();
        var retainedActiveProjectCount = requestedActiveProjects.Count(project => existingActiveProjectIds.Contains(project.Id));
        var newActiveProjectCount = requestedActiveProjects.Count(project => !existingActiveProjectIds.Contains(project.Id));
        var availableSlots = Math.Max(0, projectLimit - retainedActiveProjectCount);

        if (newActiveProjectCount > availableSlots)
        {
            return new(null, WorkspaceSaveErrorCode.ProjectLimit, $"The {plan} plan includes up to {projectLimit} projects. Existing projects are kept when your plan changes.");
        }

        var timerActive = current.Timer.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused;
        var timerExpired = current.Timer.EndsAt is { } currentEnd && currentEnd <= DateTimeOffset.UtcNow;

        var explicitTimerReclaim = request.ReclaimTimer &&
            request.Workspace.Timer.OwnerClientId == request.Workspace.ClientId &&
            SameTimerExceptOwner(current.Timer, request.Workspace.Timer);

        if (timerActive && !timerExpired && current.Timer.OwnerClientId != request.Workspace.ClientId &&
            !SameTimer(current.Timer, request.Workspace.Timer) && !explicitTimerReclaim)
        {
            return new(null, WorkspaceSaveErrorCode.TimerConflict, "A timer is already active on another device.");
        }

        request.Workspace.Plan = plan;
        PreserveSessionLabels(current, request.Workspace);

        try
        {
            var existingSessionIds = current.Sessions.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);

            var newSessions = request.Workspace.Sessions.Where(session => !existingSessionIds.Contains(session.Id)).ToArray();

            await activityArchive.ArchiveAsync(ownerId, newSessions, cancellationToken);

            var saved = await repository.SaveAsync(ownerId, request.Workspace, request.Revision, cancellationToken);

            return saved is null
                ? new(null, WorkspaceSaveErrorCode.RevisionConflict, "This workspace changed on another device. Reload it and try again.")
                : new(new(request.Workspace, saved.ETag ?? ""), null, null);
        }
        catch (WorkspaceSaveConflictException)
        {
            return new(null, WorkspaceSaveErrorCode.RevisionConflict, "This workspace changed on another device. Reload it and try again.");
        }
    }

    private async Task<WorkspaceDocument?> GetRecoveredAsync(string ownerId, CancellationToken cancellationToken)
    {
        var document = await repository.GetAsync(ownerId, cancellationToken);

        if (document?.PendingProjectArchive is not null && projectArchives is not null)
        {
            await projectArchives.RecoverAsync(ownerId, cancellationToken);
            document = await repository.GetAsync(ownerId, cancellationToken);
        }

        if (document?.PendingProjectArchive is not null)
        {
            throw new InvalidOperationException("A project archive is still being completed. Try again shortly.");
        }

        return document;
    }

    private static void PreserveSessionLabels(WorkspaceSnapshot current, WorkspaceSnapshot requested)
    {
        var previousSessions = current.Sessions.ToDictionary(session => session.Id, StringComparer.Ordinal);
        var projects = current.Projects.Concat(requested.Projects).GroupBy(project => project.Id).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var tasks = current.Tasks.Concat(requested.Tasks).GroupBy(task => task.Id).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var requestedProjectIds = requested.Projects.Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        var requestedTaskIds = requested.Tasks.Select(task => task.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var session in requested.Sessions)
        {
            previousSessions.TryGetValue(session.Id, out var previous);
            projects.TryGetValue(session.ProjectId, out var project);
            TaskRecord? task = null;

            if (session.TaskId is not null)
            {
                tasks.TryGetValue(session.TaskId, out task);
            }

            if (session.Intervals.Count == 0 && previous?.Intervals is { Count: > 0 } &&
                session.StartedAt == previous.StartedAt && session.EndedAt == previous.EndedAt)
            {
                session.Intervals = previous.Intervals;
            }

            session.ProjectName ??= previous?.ProjectName;
            session.ProjectColour ??= previous?.ProjectColour;
            session.TaskName ??= previous?.TaskName;

            if (previous is null || !requestedProjectIds.Contains(session.ProjectId))
            {
                session.ProjectName ??= project?.Name;
                session.ProjectColour ??= project?.Color;
            }

            if (session.TaskId is not null && (previous is null || !requestedTaskIds.Contains(session.TaskId)))
            {
                session.TaskName ??= task?.Title;
            }
        }
    }

    private static bool SameTimer(TimerSnapshot left, TimerSnapshot right) =>
        left.Phase == right.Phase && left.EndsAt == right.EndsAt && left.StartedAt == right.StartedAt && left.FocusSessionId == right.FocusSessionId &&
        left.PausedAt == right.PausedAt && left.RemainingSeconds == right.RemainingSeconds &&
        left.DurationSeconds == right.DurationSeconds && left.CompletedPomodoros == right.CompletedPomodoros &&
        left.TaskId == right.TaskId && left.ProjectId == right.ProjectId && left.OwnerClientId == right.OwnerClientId &&
        left.CompletedIntervals.SequenceEqual(right.CompletedIntervals);

    private static bool SameTimerExceptOwner(TimerSnapshot left, TimerSnapshot right) =>
        left.Phase == right.Phase && left.EndsAt == right.EndsAt && left.StartedAt == right.StartedAt && left.FocusSessionId == right.FocusSessionId &&
        left.PausedAt == right.PausedAt && left.RemainingSeconds == right.RemainingSeconds &&
        left.DurationSeconds == right.DurationSeconds && left.CompletedPomodoros == right.CompletedPomodoros &&
        left.TaskId == right.TaskId && left.ProjectId == right.ProjectId && left.CompletedIntervals.SequenceEqual(right.CompletedIntervals);
}
