using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class WorkspaceMigrationRepositoryTests
{
    [Test]
    public async Task CopiesStructuredWorkspaceAndRetainsItsSource()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user-1",
            Workspace = new WorkspaceSnapshot
            {
                Projects = [new ProjectRecord { Id = "p1", Name = "Structured" }]
            }
        });
        await database.SaveChangesAsync();

        var result = await new WorkspaceMigrationRepository(database).MigrateAsync("user-1", apply: true);

        Assert.That(result.Projects, Is.EqualTo(1));
        Assert.That((await database.WorkspaceProjects.SingleAsync()).Project!.Name, Is.EqualTo("Structured"));
        Assert.That((await database.Workspaces.SingleAsync()).Workspace!.Projects.Single().Name, Is.EqualTo("Structured"));
    }

    [Test]
    public async Task CopiesAndVerifiesCollectionsAndCanBeRerunWithoutRemovingSource()
    {
        await using var database = CreateDatabase();
        var workspace = new WorkspaceSnapshot
        {
            Projects = [new ProjectRecord { Id = "p1", Name = "Project" }],
            Tasks = [new TaskRecord { Id = "t1", ProjectId = "p1", Title = "Task" }],
            Sessions = [new FocusSessionRecord { Id = "s1", ProjectId = "p1" }],
            Conversations = [new ConversationRecord { Id = "c1", Messages = [new ChatMessageRecord { Text = "Hello" }] }]
        };
        var payload = JsonSerializer.Serialize(workspace, SharedCommon.JsonOptions);
        database.Workspaces.Add(new WorkspaceDocument { UserId = "user-1", Payload = payload });
        await database.SaveChangesAsync();
        var repository = new WorkspaceMigrationRepository(database);

        var preview = await repository.MigrateAsync("user-1");
        Assert.That(await database.WorkspaceProjects.CountAsync(), Is.Zero);
        var first = await repository.MigrateAsync("user-1", apply: true);
        var second = await repository.MigrateAsync("user-1", apply: true);

        Assert.That(first, Is.EqualTo(new WorkspaceMigrationResult(1, 1, 1, 1)));
        Assert.That(preview, Is.EqualTo(first));
        Assert.That(second, Is.EqualTo(first));
        Assert.That((await database.Workspaces.SingleAsync()).Payload, Is.EqualTo(payload));
        var conversation = await database.WorkspaceConversations.SingleAsync();
        Assert.That(conversation.Conversation!.Messages.Single().Text, Is.EqualTo("Hello"));
        Assert.That(conversation.UserId, Is.EqualTo("user-1"));
    }

    [Test]
    public async Task RefusesConflictingDestinationBeforeWritingAnyCollection()
    {
        await using var database = CreateDatabase();
        var workspace = new WorkspaceSnapshot { Projects = [new ProjectRecord { Id = "p1", Name = "Project" }], Tasks = [new TaskRecord { Id = "t1" }] };
        database.Workspaces.Add(new WorkspaceDocument { UserId = "user-1", Payload = JsonSerializer.Serialize(workspace, SharedCommon.JsonOptions) });
        database.WorkspaceTasks.Add(new WorkspaceTaskDocument { Id = "t1", UserId = "user-1", Task = new TaskRecord { Id = "t1", Title = "Different" } });
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await new WorkspaceMigrationRepository(database).MigrateAsync("user-1"));
        Assert.That(await database.WorkspaceProjects.CountAsync(), Is.Zero);
        Assert.That((await database.WorkspaceTasks.SingleAsync()).Task!.Title, Is.EqualTo("Different"));
    }

    private static DatabaseContext CreateDatabase() => new(new DbContextOptionsBuilder<DatabaseContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
