using API.Security;
using Shared.Enums;
using Shared.Models;

namespace API.Tests;

[TestFixture]
public sealed class WorkspaceRequestValidatorTests
{
    [Test]
    public void ValidWorkspaceIsAccepted()
    {
        Assert.That(WorkspaceRequestValidator.Validate(CreateWorkspace()), Is.Null);
    }

    [Test]
    public void DuplicateIdentifiersAreRejected()
    {
        var workspace = CreateWorkspace();
        workspace.Tasks.Add(new TaskRecord
        {
            Id = workspace.Tasks[0].Id,
            Title = "Duplicate",
            ProjectId = workspace.Projects[0].Id
        });

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
    }

    [Test]
    public void ReferencesOutsideTheSubmittedWorkspaceAreRejected()
    {
        var workspace = CreateWorkspace();
        workspace.Tasks[0].ProjectId = "another-users-project";

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
    }

    [Test]
    public void HistoricalSessionsCanReferenceRemovedOrMovedTasksAndUnassignedFocus()
    {
        var workspace = CreateWorkspace();
        workspace.Sessions[0].TaskId = "removed-task";
        workspace.Sessions.Add(new FocusSessionRecord
        {
            ProjectId = "old-project",
            TaskId = workspace.Tasks[0].Id
        });
        workspace.Sessions.Add(new FocusSessionRecord
        {
            ProjectId = ""
        });

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Null);
    }

    [Test]
    public void CoachRetentionKeepsNewestMessagesWithinWorkspaceLimit()
    {
        var workspace = CreateWorkspace();
        var conversation = new ConversationRecord
        {
            Messages = Enumerable.Range(0, WorkspaceRequestValidator.MAXIMUM_CONVERSATION_MESSAGES + 2)
                .Select(index => new ChatMessageRecord
                {
                    Role = ChatMessageRole.User,
                    Text = $"Message {index}"
                })
                .ToList()
        };
        workspace.Conversations.Add(conversation);

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
        WorkspaceRequestValidator.RetainRecentMessages(conversation);

        Assert.That(conversation.Messages, Has.Count.EqualTo(WorkspaceRequestValidator.MAXIMUM_CONVERSATION_MESSAGES));
        Assert.That(conversation.Messages[0].Text, Is.EqualTo("Message 2"));
        Assert.That(conversation.Messages[^1].Text, Is.EqualTo("Message 1001"));
        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Null);
    }

    [Test]
    public void ExcessiveTaskCollectionsAreRejected()
    {
        var workspace = CreateWorkspace();
        workspace.Tasks = Enumerable.Range(0, 10_001)
            .Select(index => new TaskRecord
            {
                Id = $"task-{index}",
                Title = "Task",
                ProjectId = workspace.Projects[0].Id
            })
            .ToList();

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
    }

    [Test]
    public void InvalidTimerBoundsAreRejected()
    {
        var workspace = CreateWorkspace();
        workspace.Timer.RemainingSeconds = int.MaxValue;

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
    }

    [TestCase(-1d)]
    [TestCase(86401d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidFocusSecondsAreRejected(double seconds)
    {
        var workspace = CreateWorkspace();
        workspace.Sessions[0].FocusSeconds = seconds;

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
    }

    [Test]
    public void FractionalFocusSecondsAreAccepted()
    {
        var workspace = CreateWorkspace();
        workspace.Sessions[0].FocusSeconds = 30.5;

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Null);
    }

    [Test]
    public void UnsafeProjectColorIsRejected()
    {
        var workspace = CreateWorkspace();
        workspace.Projects[0].Color = "red;display:none";

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
    }

    [Test]
    public void Validate_NullSessionIntervals_ReturnsRecordError()
    {
        var workspace = CreateWorkspace();
        workspace.Sessions[0].Intervals = null!;

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.EqualTo("Workspace records contain invalid fields or references."));
    }

    [Test]
    public void Validate_IntervalOutsideSession_ReturnsRecordError()
    {
        var workspace = CreateWorkspace();
        var session = workspace.Sessions[0];
        session.Intervals.Add(new FocusIntervalRecord
        {
            StartedAt = session.StartedAt.AddSeconds(-1),
            EndedAt = session.StartedAt.AddSeconds(1)
        });

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.EqualTo("Workspace records contain invalid fields or references."));
    }

    [Test]
    public void Validate_NullConversationMessages_ReturnsConversationError()
    {
        var workspace = CreateWorkspace();
        workspace.Conversations.Add(new ConversationRecord
        {
            Messages = null!
        });

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.EqualTo("Conversation data exceeds the supported limits."));
    }

    [Test]
    public void Validate_TimerTaskFromAnotherProject_ReturnsTimerError()
    {
        var workspace = CreateWorkspace();
        workspace.Projects.Add(new ProjectRecord
        {
            Id = "other-project",
            Name = "Other"
        });
        workspace.Timer.ProjectId = "other-project";
        workspace.Timer.TaskId = workspace.Tasks[0].Id;

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.EqualTo("Timer data is invalid or exceeds the supported limits."));
    }

    private static WorkspaceSnapshot CreateWorkspace()
    {
        var project = new ProjectRecord { Id = "project-1", Name = "Project" };
        var task = new TaskRecord
        {
            Id = "task-1",
            Title = "Task",
            ProjectId = project.Id
        };

        return new WorkspaceSnapshot
        {
            ClientId = "client-1",
            Projects = [project],
            Tasks = [task],
            Sessions = [new FocusSessionRecord
            {
                Id = "session-1",
                ProjectId = project.Id,
                TaskId = task.Id,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-25),
                EndedAt = DateTimeOffset.UtcNow,
                FocusMinutes = 25
            }]
        };
    }
}
