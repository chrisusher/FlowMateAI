using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;
using Shared.Enums;

namespace Services.Tests.Repositories;

public sealed class TaskRepositoryTests
{
    [Test]
    public async Task DeleteAsync_IdleSelectedTask_ClearsSelectionAndPreservesHistory()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ETag = "workspace-revision",
            ProjectsAuthoritative = true,
            TasksAuthoritative = true,
            Workspace = new WorkspaceSnapshot
            {
                Timer = new TimerSnapshot
                {
                    TaskId = "task",
                    ProjectId = "project"
                },
                Sessions = [new FocusSessionRecord
                {
                    Id = "session",
                    TaskId = "task",
                    ProjectId = "project"
                }]
            }
        });
        database.Tasks.Add(new WorkspaceTaskDocument
        {
            UserId = "user",
            Id = "task",
            ETag = "task-revision",
            Task = new TaskRecord
            {
                Id = "task",
                Title = "Important task",
                ProjectId = "project"
            }
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var result = await new TaskRepository(database).DeleteAsync("user", "task", "task-revision", "workspace-revision");

        Assert.That(result.Rejection, Is.EqualTo(TaskChangeRejection.None));
        var workspace = await database.Workspaces.AsNoTracking().SingleAsync();
        Assert.That(workspace.PendingTaskOperation, Is.Null);
        Assert.That(workspace.Workspace!.Timer.TaskId, Is.Null);
        Assert.That(workspace.Workspace.Sessions.Single().TaskName, Is.EqualTo("Important task"));
        Assert.That((await database.Tasks.AsNoTracking().SingleAsync()).IsDeleted, Is.True);
    }

    [Test]
    public async Task DeleteAsync_ActiveSelectedTask_RejectsWithoutChangingTask()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ETag = "workspace-revision",
            ProjectsAuthoritative = true,
            TasksAuthoritative = true,
            Workspace = new WorkspaceSnapshot
            {
                Timer = new TimerSnapshot
                {
                    TaskId = "task",
                    Phase = TimerPhase.Focus
                }
            }
        });
        database.Tasks.Add(new WorkspaceTaskDocument
        {
            UserId = "user",
            Id = "task",
            ETag = "revision",
            Task = new TaskRecord
            {
                Id = "task"
            }
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var result = await new TaskRepository(database).DeleteAsync("user", "task", "revision", "workspace-revision");

        Assert.That(result.Rejection, Is.EqualTo(TaskChangeRejection.TimerActive));
        Assert.That((await database.Tasks.SingleAsync()).IsDeleted, Is.False);
        Assert.That((await database.Workspaces.SingleAsync()).PendingTaskOperation, Is.Null);
    }

    private static DatabaseContext CreateDatabase() => new(new DbContextOptionsBuilder<DatabaseContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
