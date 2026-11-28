using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

public sealed class TaskCutoverRepositoryTests
{
    [Test]
    public async Task ApplyAsync_VerifiedLegacyTasks_PreservesOwnerRecordsAndRemovesSnapshotCopy()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ETag = "revision",
            Workspace = new WorkspaceSnapshot
            {
                Tasks = [
                    new TaskRecord
                    {
                        Id = "b",
                        Title = "First"
                    },
                    new TaskRecord
                    {
                        Id = "a",
                        Title = "Second"
                    }
                ]
            }
        });
        database.Tasks.Add(new WorkspaceTaskDocument
        {
            UserId = "other",
            Id = "other-task",
            Task = new TaskRecord
            {
                Id = "other-task"
            }
        });
        await database.SaveChangesAsync();
        var cutover = new TaskCutoverRepository(database);

        var preview = await cutover.PreviewAsync("user");
        Assert.That(preview.TaskCount, Is.EqualTo(2));
        Assert.That(await database.Tasks.CountAsync(item => item.UserId == "user"), Is.Zero);
        await cutover.ApplyAsync("user", preview.Revision);

        var stored = await database.Workspaces.AsNoTracking().SingleAsync();
        Assert.That(stored.TasksAuthoritative, Is.True);
        Assert.That(stored.Workspace!.Tasks, Is.Empty);
        var reader = new WorkspaceItemsRepository(database);
        Assert.That((await reader.ListTasksAsync("user")).Select(task => task.Id), Is.EqualTo(new[] { "b", "a" }));
        Assert.That((await reader.ListTasksAsync("other")).Select(task => task.Id), Is.Empty);
        Assert.That(await database.Tasks.CountAsync(item => item.UserId == "other"), Is.EqualTo(1));
    }

    [Test]
    public async Task ApplyAsync_StaleRevisionOrConflictingDestination_LeavesAuthorityUnchanged()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ETag = "revision",
            Workspace = new WorkspaceSnapshot
            {
                Tasks = [new TaskRecord
                {
                    Id = "task",
                    Title = "Source"
                }]
            }
        });
        await database.SaveChangesAsync();
        var cutover = new TaskCutoverRepository(database);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cutover.ApplyAsync("user", "stale"));
        Assert.That(await database.Tasks.CountAsync(), Is.Zero);

        database.Tasks.Add(new WorkspaceTaskDocument
        {
            UserId = "user",
            Id = "task",
            Task = new TaskRecord
            {
                Id = "task",
                Title = "Different"
            }
        });
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cutover.PreviewAsync("user"));
        Assert.That((await database.Workspaces.SingleAsync()).TasksAuthoritative, Is.False);
    }

    private static DatabaseContext CreateDatabase() => new(new DbContextOptionsBuilder<DatabaseContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
