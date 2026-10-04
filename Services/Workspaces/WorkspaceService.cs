using ChrisUsher.Core.Shared;
using Services.Repositories;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace Services.Workspaces;

public sealed class WorkspaceService(IWorkspaceRepository repository, IActivityArchiveRepository activityArchive, IBillingRepository billingRepository) : IWorkspaceService
{
    public async Task<WorkspaceResponse> GetAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetAsync(ownerId, cancellationToken);

        var snapshot = document is null ? new WorkspaceSnapshot() : Deserialize(document.Payload);
        var entitlement = await billingRepository.GetEntitlementAsync(ownerId, cancellationToken);
        snapshot.Plan = BillingPlanExtensions.ParseOrFree(entitlement.Plan);

        return new(snapshot, document?.ETag ?? "");
    }

    public async Task<WorkspaceSaveResult> SaveAsync(string ownerId, WorkspaceSaveRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAsync(ownerId, cancellationToken);

        if (existing is not null && (string.IsNullOrEmpty(request.Revision) || !string.Equals(existing.ETag, request.Revision, StringComparison.Ordinal)))
        {
            return new(null, WorkspaceSaveErrorCode.RevisionConflict, "This workspace changed on another device. Reload it and try again.");
        }

        var current = existing is null ? new WorkspaceSnapshot() : Deserialize(existing.Payload);
        var entitlement = await billingRepository.GetEntitlementAsync(ownerId, cancellationToken);
        var plan = BillingPlanExtensions.ParseOrFree(entitlement.Plan);
        var projectLimit = plan == BillingPlan.Pro ? 25 : 3;
        var existingProjectIds = current.Projects.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var newProjects = request.Workspace.Projects.Count(p => !existingProjectIds.Contains(p.Id));
        var availableSlots = Math.Max(0, projectLimit - current.Projects.Count);

        if (newProjects > availableSlots)
        {
            return new(null, WorkspaceSaveErrorCode.ProjectLimit, $"The {plan} plan includes up to {projectLimit} projects. Existing projects are kept when your plan changes.");
        }

        var timerActive = current.Timer.Phase is TimerPhase.Focus or TimerPhase.ShortBreak or TimerPhase.LongBreak or TimerPhase.Paused;
        var timerExpired = current.Timer.EndsAt is { } currentEnd && currentEnd <= DateTimeOffset.UtcNow;

        if (timerActive && !timerExpired && current.Timer.OwnerClientId != request.Workspace.ClientId && !SameTimer(current.Timer, request.Workspace.Timer))
        {
            return new(null, WorkspaceSaveErrorCode.TimerConflict, "A timer is already active on another device.");
        }

        request.Workspace.Plan = plan;

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

    private static WorkspaceSnapshot Deserialize(string payload) =>
        JsonSerializer.Deserialize<WorkspaceSnapshot>(payload, SharedCommon.JsonOptions) ?? new();

    private static bool SameTimer(TimerSnapshot left, TimerSnapshot right) =>
        left.Phase == right.Phase && left.EndsAt == right.EndsAt && left.StartedAt == right.StartedAt &&
        left.PausedAt == right.PausedAt && left.RemainingSeconds == right.RemainingSeconds &&
        left.DurationSeconds == right.DurationSeconds && left.CompletedPomodoros == right.CompletedPomodoros &&
        left.TaskId == right.TaskId && left.ProjectId == right.ProjectId && left.OwnerClientId == right.OwnerClientId &&
        left.CompletedIntervals.SequenceEqual(right.CompletedIntervals);
}
