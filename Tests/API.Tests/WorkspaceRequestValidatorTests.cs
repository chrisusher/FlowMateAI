using API.Security;
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
        workspace.Tasks.Add(new TaskRecord { Id = workspace.Tasks[0].Id, Title = "Duplicate", ProjectId = workspace.Projects[0].Id });

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
    public void ExcessiveTaskCollectionsAreRejected()
    {
        var workspace = CreateWorkspace();
        workspace.Tasks = Enumerable.Range(0, 10_001)
            .Select(index => new TaskRecord { Id = $"task-{index}", Title = "Task", ProjectId = workspace.Projects[0].Id })
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

    [Test]
    public void UnsafeProjectColorIsRejected()
    {
        var workspace = CreateWorkspace();
        workspace.Projects[0].Color = "red;display:none";

        Assert.That(WorkspaceRequestValidator.Validate(workspace), Is.Not.Null);
    }

    private static WorkspaceSnapshot CreateWorkspace()
    {
        var project = new ProjectRecord { Id = "project-1", Name = "Project" };
        var task = new TaskRecord { Id = "task-1", Title = "Task", ProjectId = project.Id };

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
