using Shared.Enums;
using Shared.Models;

namespace API.Security;

internal static class WorkspaceRequestValidator
{
    internal const int MaximumPayloadBytes = 2 * 1024 * 1024;

    internal static string? Validate(WorkspaceSnapshot? workspace)
    {
        if (workspace is null)
        {
            return "Workspace data is missing or invalid.";
        }

        if (!HasLength(workspace.DisplayName, 0, 120) || !HasLength(workspace.TimeZone, 0, 100) || !HasLength(workspace.ClientId, 1, 128) ||
            (!string.IsNullOrEmpty(workspace.TimeZone) && !IsSupportedTimeZone(workspace.TimeZone)))
        {
            return "Workspace profile fields exceed their supported limits.";
        }

        if (!Enum.IsDefined(workspace.FirstRunState) ||
            !Bounded(workspace.Projects, 1000) || !Bounded(workspace.Tasks, 10_000) ||
            !Bounded(workspace.Sessions, 50_000) || !Bounded(workspace.Conversations, 1000) ||
            workspace.Timer is null)
        {
            return "Workspace data exceeds the supported limits.";
        }

        if (!UniqueIds(workspace.Projects.Select(project => project?.Id)) ||
            !UniqueIds(workspace.Tasks.Select(task => task?.Id)) ||
            !UniqueIds(workspace.Sessions.Select(session => session?.Id)) ||
            !UniqueIds(workspace.Conversations.Select(conversation => conversation?.Id)))
        {
            return "Workspace record identifiers must be present and unique.";
        }

        var projectIds = workspace.Projects.Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        var tasksById = workspace.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);

        if (workspace.Projects.Any(project => !HasLength(project.Name, 1, 200) || !IsHexColor(project.Color)) ||
            workspace.Tasks.Any(task => !HasLength(task.Title, 1, 500) || !HasLength(task.ProjectId, 1, 128) ||
                !projectIds.Contains(task.ProjectId) || !Enum.IsDefined(task.Priority) || task.FocusMinutesToday is < 0 or > 1440) ||
            workspace.Sessions.Any(session => !HasLength(session.ProjectId, 1, 128) || !projectIds.Contains(session.ProjectId) ||
                !HasLength(session.TaskId, 0, 128) || (session.TaskId is not null &&
                    (!tasksById.TryGetValue(session.TaskId, out var task) || task.ProjectId != session.ProjectId)) ||
                session.EndedAt < session.StartedAt || session.FocusMinutes is < 0 or > 1440))
        {
            return "Workspace records contain invalid fields or references.";
        }

        foreach (var conversation in workspace.Conversations)
        {
            if (!HasLength(conversation.Title, 0, 200) || !Bounded(conversation.Messages, 1000) ||
                conversation.Messages.Any(message => message is null || !Enum.IsDefined(message.Role) || !HasLength(message.Text, 1, 8000)))
            {
                return "Conversation data exceeds the supported limits.";
            }
        }

        var timer = workspace.Timer;

        if (!Enum.IsDefined(timer.Phase) || timer.RemainingSeconds is < 0 or > 172_800 ||
            timer.DurationSeconds is < 0 or > 172_800 || timer.CompletedPomodoros is < 0 or > 10_000 ||
            !HasLength(timer.ProjectId, 0, 128) || !HasLength(timer.TaskId, 0, 128) || !HasLength(timer.OwnerClientId, 0, 128) ||
            (!string.IsNullOrEmpty(timer.ProjectId) && !projectIds.Contains(timer.ProjectId)) ||
            (timer.TaskId is not null && (!tasksById.TryGetValue(timer.TaskId, out var task) || task.ProjectId != timer.ProjectId)) ||
            !Bounded(timer.CompletedIntervals, 100) || !ValidIntervals(timer.CompletedIntervals))
        {
            return "Timer data is invalid or exceeds the supported limits.";
        }

        return null;
    }

    private static bool HasLength(string? value, int minimum, int maximum) =>
        value is null ? minimum == 0 : value.Length >= minimum && value.Length <= maximum;

    private static bool IsHexColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static bool IsSupportedTimeZone(string timeZone)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timeZone);

            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static bool ValidIntervals(ICollection<FocusIntervalRecord>? intervals)
    {
        if (intervals is null || intervals.Any(interval => interval is null))
        {
            return false;
        }


        var ordered = intervals.OrderBy(interval => interval.StartedAt).ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].EndedAt < ordered[index].StartedAt ||
                ordered[index].EndedAt - ordered[index].StartedAt > TimeSpan.FromHours(24) ||
                (index > 0 && ordered[index].StartedAt < ordered[index - 1].EndedAt))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Bounded<T>(ICollection<T>? values, int maximum) =>
        values is not null && values.Count <= maximum;

    private static bool UniqueIds(IEnumerable<string?> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (!HasLength(value, 1, 128) || !seen.Add(value!))
            {
                return false;
            }
        }

        return true;
    }
}
