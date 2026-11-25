namespace API.Security;

internal static class WorkspaceRequestValidator
{
    internal const int MAXIMUM_PAYLOAD_BYTES = 2 * 1024 * 1024;
    internal const int MAXIMUM_CONVERSATION_MESSAGES = 1000;

    internal static void RetainRecentMessages(ConversationRecord conversation)
    {
        var excess = conversation.Messages.Count - MAXIMUM_CONVERSATION_MESSAGES;

        if (excess > 0)
        {
            conversation.Messages.RemoveRange(0, excess);
        }
    }

    // Keep field rules local to each record type. Cross-record references are checked
    // separately, after collection and identifier validation has succeeded.
    private static readonly Func<WorkspaceSnapshot, bool>[] ProfileRules =
    [
        workspace => HasLength(workspace.DisplayName, 0, 120),
        workspace => HasLength(workspace.TimeZone, 0, 100),
        workspace => HasLength(workspace.ClientId, 1, 128),
        workspace => string.IsNullOrEmpty(workspace.TimeZone) || IsSupportedTimeZone(workspace.TimeZone)
    ];

    private static readonly Func<WorkspaceSnapshot, bool>[] CollectionRules =
    [
        workspace => Enum.IsDefined(workspace.FirstRunState),
        workspace => Bounded(workspace.Projects, 1000),
        workspace => Bounded(workspace.Tasks, 10_000),
        workspace => Bounded(workspace.Sessions, 50_000),
        workspace => Bounded(workspace.Conversations, 1000),
        workspace => workspace.Timer is not null
    ];

    private static readonly Func<WorkspaceSnapshot, bool>[] IdentifierRules =
    [
        workspace => UniqueIds(workspace.Projects.Select(project => project?.Id)),
        workspace => UniqueIds(workspace.Tasks.Select(task => task?.Id)),
        workspace => UniqueIds(workspace.Sessions.Select(session => session?.Id)),
        workspace => UniqueIds(workspace.Conversations.Select(conversation => conversation?.Id))
    ];

    private static readonly Func<ProjectRecord, bool>[] ProjectRules =
    [
        project => HasLength(project.Name, 1, 200),
        project => IsHexColor(project.Color)
    ];

    private static readonly Func<TaskRecord, bool>[] TaskRules =
    [
        task => HasLength(task.Title, 1, 500),
        task => HasLength(task.ProjectId, 1, 128),
        task => Enum.IsDefined(task.Priority),
        task => task.FocusMinutesToday is >= 0 and <= 1440
    ];

    private static readonly Func<FocusSessionRecord, bool>[] SessionRules =
    [
        session => HasLength(session.ProjectId, 0, 128),
        session => HasLength(session.TaskId, 0, 128),
        session => HasLength(session.ProjectName, 0, 200),
        session => HasLength(session.TaskName, 0, 500),
        session => session.ProjectColour is null || IsHexColor(session.ProjectColour),
        session => session.EndedAt >= session.StartedAt,
        session => session.FocusMinutes is >= 0 and <= 1440,
        session => session.FocusSeconds is not { } seconds || (double.IsFinite(seconds) && seconds is >= 0 and <= 86_400),
        session => Bounded(session.Intervals, 101),
        session => ValidIntervals(session.Intervals),
        session => session.Intervals.All(interval => interval.StartedAt >= session.StartedAt && interval.EndedAt <= session.EndedAt),
        session => session.PlannedSeconds is null or (> 0 and <= 86_400)
    ];

    private static readonly Func<ConversationRecord, bool>[] ConversationRules =
    [
        conversation => HasLength(conversation.Title, 0, 200),
        conversation => Bounded(conversation.Messages, MAXIMUM_CONVERSATION_MESSAGES),
        conversation => conversation.Messages.All(message => message is not null && Enum.IsDefined(message.Role) && HasLength(message.Text, 1, 8000))
    ];

    private static readonly Func<TimerSnapshot, bool>[] TimerRules =
    [
        timer => Enum.IsDefined(timer.Phase),
        timer => timer.RemainingSeconds is >= 0 and <= 172_800,
        timer => timer.DurationSeconds is >= 0 and <= 172_800,
        timer => timer.CompletedPomodoros is >= 0 and <= 10_000,
        timer => HasLength(timer.ProjectId, 0, 128),
        timer => HasLength(timer.TaskId, 0, 128),
        timer => HasLength(timer.OwnerClientId, 0, 128),
        timer => Bounded(timer.CompletedIntervals, 100),
        timer => ValidIntervals(timer.CompletedIntervals)
    ];

    internal static string? Validate(WorkspaceSnapshot? workspace)
    {
        if (workspace is null)
        {
            return "Workspace data is missing or invalid.";
        }

        if (!MatchesRules(workspace, ProfileRules))
        {
            return "Workspace profile fields exceed their supported limits.";
        }

        if (!MatchesRules(workspace, CollectionRules))
        {
            return "Workspace data exceeds the supported limits.";
        }

        if (!MatchesRules(workspace, IdentifierRules))
        {
            return "Workspace record identifiers must be present and unique.";
        }

        var projectIds = workspace.Projects.Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        var tasksById = workspace.Tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);

        if (!AllRecordsValid(workspace.Projects, ProjectRules) ||
            !AllRecordsValid(workspace.Tasks, TaskRules) ||
            !AllRecordsValid(workspace.Sessions, SessionRules) ||
            workspace.Tasks.Any(task => !projectIds.Contains(task.ProjectId)))
        {
            return "Workspace records contain invalid fields or references.";
        }

        if (!AllRecordsValid(workspace.Conversations, ConversationRules))
        {
            return "Conversation data exceeds the supported limits.";
        }

        if (!MatchesRules(workspace.Timer, TimerRules) || !HasValidTimerReferences(workspace.Timer, projectIds, tasksById))
        {
            return "Timer data is invalid or exceeds the supported limits.";
        }

        return null;
    }

    private static bool HasValidTimerReferences(TimerSnapshot timer, HashSet<string> projectIds, Dictionary<string, TaskRecord> tasksById) =>
        (string.IsNullOrEmpty(timer.ProjectId) || projectIds.Contains(timer.ProjectId)) &&
        (timer.TaskId is null || (tasksById.TryGetValue(timer.TaskId, out var task) && task.ProjectId == timer.ProjectId));

    private static bool AllRecordsValid<T>(IEnumerable<T> records, Func<T, bool>[] rules) =>
        records.All(record => MatchesRules(record, rules));

    // Rule order matters for collections: bounds and null checks precede enumeration.
    private static bool MatchesRules<T>(T record, Func<T, bool>[] rules) =>
        rules.All(rule => rule(record));

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
