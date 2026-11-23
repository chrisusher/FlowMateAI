using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class WorkspaceItemsRepositoryTests
{
    [Test]
    public void CollectionListQueriesTranslateForCosmos()
    {
        using var database = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
            .UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "tests").Options);

        Assert.DoesNotThrow(() => ListQuery(database.Projects));
        Assert.DoesNotThrow(() => ListQuery(database.Tasks));
        Assert.DoesNotThrow(() => ListQuery(database.Sessions));
        Assert.DoesNotThrow(() => ListQuery(database.Conversations));
    }

    private static string ListQuery<T>(DbSet<T> set) where T : class, IWorkspaceItemDocument =>
        set.AsNoTracking().Where(item => item.UserId == "user-1").OrderBy(item => item.Position).ToQueryString();

    [Test]
    public async Task ListReadsLegacyAndStaleCollectionsFromTheAuthoritativeWorkspace()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new DatabaseContext(options);

        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user-1",
            Workspace = new WorkspaceSnapshot
            {
                Projects =
                [
                    new ProjectRecord
                    {
                        Id = "project",
                        Name = "Current"
                    }
                ]
            }
        });
        await database.SaveChangesAsync();
        var reader = new WorkspaceItemsRepository(database);

        Assert.That((await reader.ListProjectsAsync("user-1")).Single().Name, Is.EqualTo("Current"));
        
        database.Projects.Add(new WorkspaceProjectDocument
        {
            UserId = "user-1",
            Id = "project",
            Project = new ProjectRecord
            {
                Id = "project",
                Name = "Stale"
            }
        });
        await database.SaveChangesAsync();

        Assert.That((await reader.ListProjectsAsync("user-1")).Single().Name, Is.EqualTo("Current"));
        Assert.That(await reader.ListProjectsAsync("other"), Is.Empty);
    }

    [Test]
    public async Task SaveSynchronisesCollectionsAndReadsOnlyTheOwnersRecords()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new DatabaseContext(options);
        var workspace = new WorkspaceSnapshot
        {
            Projects =
            [
                new ProjectRecord
                {
                    Id = "p1",
                    Name = "First"
                },
                new ProjectRecord
                {
                    Id = "p2",
                    Name = "Second"
                }
            ],
            Tasks = [new TaskRecord
                {
                    Id = "t1",
                    Title = "Task"
                }],
            Sessions = [new FocusSessionRecord
                {
                    Id = "s1",
                    FocusMinutes = 10
                }],
            Conversations = [new ConversationRecord
                {
                    Id = "c1",
                    Title = "Chat"
                }]
        };
        var writer = new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database));
        var saved = await writer.SaveAsync("user-1", workspace, null);
        Assert.That(saved, Is.Not.Null);
        var reader = new WorkspaceItemsRepository(database);

        Assert.That((await reader.ListProjectsAsync("user-1")).Select(item => item.Id), Is.EqualTo(new[] { "p1", "p2" }));
        Assert.That((await reader.GetProjectAsync("user-1", "p1"))!.Name, Is.EqualTo("First"));
        Assert.That((await reader.ListTasksAsync("user-1")).Single().Id, Is.EqualTo("t1"));
        Assert.That((await reader.GetTaskAsync("user-1", "t1"))!.Title, Is.EqualTo("Task"));
        Assert.That((await reader.ListSessionsAsync("user-1")).Single().Id, Is.EqualTo("s1"));
        Assert.That((await reader.GetSessionAsync("user-1", "s1"))!.FocusMinutes, Is.EqualTo(10));
        Assert.That((await reader.ListConversationsAsync("user-1")).Single().Id, Is.EqualTo("c1"));
        Assert.That((await reader.GetConversationAsync("user-1", "c1"))!.Title, Is.EqualTo("Chat"));
        Assert.That(await reader.ListProjectsAsync("other"), Is.Empty);
        Assert.That(await reader.ListTasksAsync("other"), Is.Empty);
        Assert.That(await reader.ListSessionsAsync("other"), Is.Empty);
        Assert.That(await reader.ListConversationsAsync("other"), Is.Empty);
        Assert.That(await reader.GetProjectAsync("other", "p1"), Is.Null);
        Assert.That(await reader.GetTaskAsync("other", "t1"), Is.Null);
        Assert.That(await reader.GetSessionAsync("other", "s1"), Is.Null);
        Assert.That(await reader.GetConversationAsync("other", "c1"), Is.Null);
        Assert.That(await reader.GetProjectAsync("user-1", "missing"), Is.Null);

        var source = await database.Workspaces.SingleAsync();
        source.ETag = "revision";
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        workspace.Projects.RemoveAt(0);
        workspace.Projects[0].Name = "Updated";
        workspace.Tasks.Clear();
        workspace.Sessions[0].FocusMinutes = 20;
        workspace.Conversations[0].Title = "Updated chat";
        Assert.That(await writer.SaveAsync("user-1", workspace, "revision"), Is.Not.Null);

        Assert.That((await reader.ListProjectsAsync("user-1")).Select(item => item.Name), Is.EqualTo(new[] { "Updated" }));
        Assert.That(await reader.GetProjectAsync("user-1", "p1"), Is.Null);
        Assert.That(await reader.ListTasksAsync("user-1"), Is.Empty);
        Assert.That((await reader.GetSessionAsync("user-1", "s1"))!.FocusMinutes, Is.EqualTo(20));
        Assert.That((await reader.GetConversationAsync("user-1", "c1"))!.Title, Is.EqualTo("Updated chat"));
    }
}
