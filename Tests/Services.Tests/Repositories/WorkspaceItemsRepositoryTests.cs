using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class WorkspaceItemsRepositoryTests
{
    [Test]
    public async Task AuthoritativeProjectReads_UnorderedProjects_ReturnOwnerProjectsInDeterministicOrder()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new DatabaseContext(options);
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user-1",
            ProjectsAuthoritative = true,
            Workspace = new WorkspaceSnapshot()
        });
        foreach (var (user, id, position) in new[] { ("user-1", "b", 1), ("user-1", "a", 1), ("user-1", "c", 0), ("other", "x", 0) })
        {
            database.Projects.Add(new WorkspaceProjectDocument
            {
                UserId = user,
                Id = id,
                Position = position,
                Project = new ProjectRecord
                {
                    Id = id
                }
            });
        }
        await database.SaveChangesAsync();

        var workspace = await new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database)).GetAsync("user-1");
        var projects = await new WorkspaceItemsRepository(database).ListProjectsAsync("user-1");
        var report = await new WorkspaceReportRepository(database).GetWorkspaceAsync("user-1");
        var expected = new[] { "c", "a", "b" };
        Assert.That(workspace!.Workspace!.Projects.Select(project => project.Id), Is.EqualTo(expected));
        Assert.That(projects.Select(project => project.Id), Is.EqualTo(expected));
        Assert.That(report.Workspace.Projects.Select(project => project.Id), Is.EqualTo(expected));
    }

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
    public async Task IndividualReadsUseAuthoritativeRecordsForLegacyAndStaleProjections()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new DatabaseContext(options);
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user-1",
            Workspace = new WorkspaceSnapshot
            {
                Projects = [new ProjectRecord
                {
                    Id = "p",
                    Name = "Current"
                }],
                Tasks = [new TaskRecord
                {
                    Id = "t",
                    Title = "Current"
                }],
                Sessions = [new FocusSessionRecord
                {
                    Id = "s",
                    FocusMinutes = 20
                }],
                Conversations = [new ConversationRecord
                {
                    Id = "c",
                    Title = "Current"
                }]
            }
        });
        await database.SaveChangesAsync();
        var reader = new WorkspaceItemsRepository(database);

        async Task AssertCurrentRecordsAsync()
        {
            Assert.That((await reader.GetProjectAsync("user-1", "p"))?.Name, Is.EqualTo("Current"));
            Assert.That((await reader.GetTaskAsync("user-1", "t"))?.Title, Is.EqualTo("Current"));
            Assert.That((await reader.GetSessionAsync("user-1", "s"))?.FocusMinutes, Is.EqualTo(20));
            Assert.That((await reader.GetConversationAsync("user-1", "c"))?.Title, Is.EqualTo("Current"));
        }

        await AssertCurrentRecordsAsync();

        database.Projects.AddRange(
            new WorkspaceProjectDocument
            {
                UserId = "user-1",
                Id = "p",
                Project = new ProjectRecord
                {
                    Id = "p",
                    Name = "Stale"
                }
            },
            new WorkspaceProjectDocument
            {
                UserId = "user-1",
                Id = "deleted",
                Project = new ProjectRecord
                {
                    Id = "deleted"
                }
            });
        database.Tasks.AddRange(
            new WorkspaceTaskDocument
            {
                UserId = "user-1",
                Id = "t",
                Task = new TaskRecord
                {
                    Id = "t",
                    Title = "Stale"
                }
            },
            new WorkspaceTaskDocument
            {
                UserId = "user-1",
                Id = "deleted",
                Task = new TaskRecord
                {
                    Id = "deleted"
                }
            });
        database.Sessions.AddRange(
            new WorkspaceSessionDocument
            {
                UserId = "user-1",
                Id = "s",
                Session = new FocusSessionRecord
                {
                    Id = "s",
                    FocusMinutes = 10
                }
            },
            new WorkspaceSessionDocument
            {
                UserId = "user-1",
                Id = "deleted",
                Session = new FocusSessionRecord
                {
                    Id = "deleted"
                }
            });
        database.Conversations.AddRange(
            new WorkspaceConversationDocument
            {
                UserId = "user-1",
                Id = "c",
                Conversation = new ConversationRecord
                {
                    Id = "c",
                    Title = "Stale"
                }
            },
            new WorkspaceConversationDocument
            {
                UserId = "user-1",
                Id = "deleted",
                Conversation = new ConversationRecord
                {
                    Id = "deleted"
                }
            });
        await database.SaveChangesAsync();

        await AssertCurrentRecordsAsync();
        Assert.That(await reader.GetProjectAsync("user-1", "deleted"), Is.Null);
        Assert.That(await reader.GetTaskAsync("user-1", "deleted"), Is.Null);
        Assert.That(await reader.GetSessionAsync("user-1", "deleted"), Is.Null);
        Assert.That(await reader.GetConversationAsync("user-1", "deleted"), Is.Null);
        Assert.That(await reader.GetProjectAsync("other", "p"), Is.Null);
        Assert.That(await reader.GetTaskAsync("other", "t"), Is.Null);
        Assert.That(await reader.GetSessionAsync("other", "s"), Is.Null);
        Assert.That(await reader.GetConversationAsync("other", "c"), Is.Null);
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
