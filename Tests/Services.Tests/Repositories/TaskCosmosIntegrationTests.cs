using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

public sealed class TaskCosmosIntegrationTests
{
    [Test]
    public async Task AuthoritativeTaskReads_DefaultCosmosIndex_ReturnOrderedOwnerTasks()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            TasksAuthoritative = true,
            Workspace = new WorkspaceSnapshot()
        });

        foreach (var (user, id, position) in new[] { ("user", "b", 1), ("user", "a", 1), ("user", "c", 0), ("other", "x", 0) })
        {
            database.Tasks.Add(new WorkspaceTaskDocument
            {
                UserId = user,
                Id = id,
                Position = position,
                Task = new TaskRecord
                {
                    Id = id
                }
            });
        }
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var workspace = await new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database)).GetAsync("user");
        var tasks = await new WorkspaceItemsRepository(database).ListTasksAsync("user");
        var report = await new WorkspaceReportRepository(database).GetWorkspaceAsync("user");
        var expected = new[] { "c", "a", "b" };
        Assert.That(workspace!.Workspace!.Tasks.Select(task => task.Id), Is.EqualTo(expected));
        Assert.That(tasks.Select(task => task.Id), Is.EqualTo(expected));
        Assert.That(report.Workspace.Tasks.Select(task => task.Id), Is.EqualTo(expected));
    }
    [Test]
    public async Task TaskWrites_ConcurrentClientsAndReload_PreserveIndependentChangesAndRejectStaleEdits()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var first = testDatabase.CreateContext();
        first.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ProjectsAuthoritative = true,
            TasksAuthoritative = true,
            Workspace = new WorkspaceSnapshot()
        });
        first.Projects.Add(new WorkspaceProjectDocument
        {
            UserId = "user",
            Id = "project",
            Project = new ProjectRecord
            {
                Id = "project",
                Name = "Project"
            }
        });
        await first.SaveChangesAsync();
        first.ChangeTracker.Clear();
        var workspace = await first.Workspaces.AsNoTracking().SingleAsync();
        var tasks = new TaskRepository(first);
        var created = await tasks.CreateAsync("user", new TaskRecord
        {
            Id = "task",
            Title = "Original",
            ProjectId = "project"
        }, workspace.ETag!);
        Assert.That(created.Rejection, Is.EqualTo(Shared.Enums.TaskChangeRejection.None));
        Assert.That(created.Revision, Is.Not.Null.And.Not.Empty);
        Assert.That(created.WorkspaceRevision, Is.Not.Null.And.Not.Empty);
        var retried = await tasks.CreateAsync("user", created.Task!, workspace.ETag!);
        Assert.That(retried.Rejection, Is.EqualTo(Shared.Enums.TaskChangeRejection.None));
        Assert.That(await first.Tasks.AsNoTracking().CountAsync(item => item.UserId == "user"), Is.EqualTo(1));

        await using var second = testDatabase.CreateContext();
        var other = new TaskRepository(second);
        var stale = await other.GetAsync("user", "task");
        var changed = await tasks.UpdateAsync("user", new TaskRecord
        {
            Id = "task",
            Title = "Changed",
            ProjectId = "project"
        }, created.Revision!, created.WorkspaceRevision!);
        Assert.That(changed.Rejection, Is.EqualTo(Shared.Enums.TaskChangeRejection.None));
        var rejected = await other.UpdateAsync("user", new TaskRecord
        {
            Id = "task",
            Title = "Stale",
            ProjectId = "project"
        }, stale.Revision!, created.WorkspaceRevision!);
        Assert.That(rejected.Rejection, Is.EqualTo(Shared.Enums.TaskChangeRejection.Conflict));

        first.ChangeTracker.Clear();
        var workspaces = new WorkspaceRepository(first, new WorkspaceCollectionSynchronizer(first));
        var staleWorkspace = new WorkspaceSnapshot
        {
            Tasks = [created.Task!]
        };
        var saved = await workspaces.SaveAsync("user", staleWorkspace, created.WorkspaceRevision);
        Assert.That(saved, Is.Not.Null);
        first.ChangeTracker.Clear();
        var reloaded = await workspaces.GetAsync("user");
        Assert.That(reloaded!.Workspace!.Tasks.Single().Title, Is.EqualTo("Changed"));
        Assert.That(reloaded.Workspace.Tasks.Single().Id, Is.EqualTo("task"));
    }
    [Test]
    public async Task RecoverAsync_InterruptedDelete_CompletesOnceAndPreservesOtherAccounts()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
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

        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        foreach (var user in new[] { "user", "other" })
        {
            database.Tasks.Add(new WorkspaceTaskDocument
            {
                UserId = user,
                Id = "task",
                Task = new TaskRecord
                {
                    Id = "task",
                    Title = "Keep history",
                    ProjectId = "project"
                }
            });
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
        }

        var task = await database.Tasks.AsNoTracking().SingleAsync(item => item.UserId == "user" && item.Id == "task");
        var workspace = await database.Workspaces.SingleAsync(item => item.UserId == "user");
        workspace.PendingTaskOperation = new TaskGuardOperation
        {
            OperationId = "operation",
            Kind = Shared.Enums.TaskGuardOperationKind.Delete,
            TaskId = "task",
            OriginRevision = task.ETag
        };
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var repository = new TaskRepository(database);
        var result = await repository.RecoverAsync("user");
        Assert.That(result!.Rejection, Is.EqualTo(Shared.Enums.TaskChangeRejection.None));
        Assert.That(await repository.RecoverAsync("user"), Is.Null);
        database.ChangeTracker.Clear();
        var after = await database.Workspaces.AsNoTracking().SingleAsync(item => item.UserId == "user");
        Assert.That(after.PendingTaskOperation, Is.Null);
        Assert.That(after.Workspace!.Timer.TaskId, Is.Null);
        Assert.That(after.Workspace.Sessions.Single().TaskName, Is.EqualTo("Keep history"));
        Assert.That((await database.Tasks.AsNoTracking().SingleAsync(item => item.UserId == "user")).IsDeleted, Is.True);
        Assert.That((await database.Tasks.AsNoTracking().SingleAsync(item => item.UserId == "other")).IsDeleted, Is.False);
    }
    [Test]
    public async Task UpdateAsync_ConcurrentEditsOfSameRevision_OneSucceedsAndOneConflicts()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var setup = testDatabase.CreateContext();
        setup.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ProjectsAuthoritative = true,
            TasksAuthoritative = true,
            Workspace = new WorkspaceSnapshot()
        });
        setup.Tasks.Add(new WorkspaceTaskDocument
        {
            UserId = "user",
            Id = "task",
            Task = new TaskRecord
            {
                Id = "task",
                Title = "Original",
                ProjectId = "project"
            }
        });
        await setup.SaveChangesAsync();
        setup.ChangeTracker.Clear();
        var revision = (await setup.Tasks.AsNoTracking().SingleAsync()).ETag!;
        var workspaceRevision = (await setup.Workspaces.AsNoTracking().SingleAsync()).ETag!;
        await using var first = testDatabase.CreateContext();
        await using var second = testDatabase.CreateContext();

        var results = await Task.WhenAll(
            new TaskRepository(first).UpdateAsync("user", new TaskRecord
            {
                Id = "task",
                Title = "First",
                ProjectId = "project"
            }, revision, workspaceRevision),
            new TaskRepository(second).UpdateAsync("user", new TaskRecord
            {
                Id = "task",
                Title = "Second",
                ProjectId = "project"
            }, revision, workspaceRevision));

        Assert.That(results.Select(result => result.Rejection), Is.EquivalentTo(new[]
        {
            Shared.Enums.TaskChangeRejection.None,
            Shared.Enums.TaskChangeRejection.Conflict
        }));
        setup.ChangeTracker.Clear();
        var persisted = await setup.Tasks.AsNoTracking().SingleAsync();
        Assert.That(persisted.Task!.Title, Is.EqualTo(results.Single(result => result.Rejection == Shared.Enums.TaskChangeRejection.None).Task!.Title));
    }
}
