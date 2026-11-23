using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class WorkspaceCollectionResetTests
{
    [Test]
    public async Task DeletesOnlyTheRequestedUsersCollectionRecordsAndCanBeRetried()
    {
        await using var database = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

        foreach (var user in new[] { "first", "second" })
        {
            database.Projects.Add(new WorkspaceProjectDocument
            {
                Id = user + "-project",
                UserId = user,
                Project = new ProjectRecord
                {
                    Id = user + "-project"
                }
            });
            database.Tasks.Add(new WorkspaceTaskDocument
            {
                Id = user + "-task",
                UserId = user,
                Task = new TaskRecord
                {
                    Id = user + "-task"
                }
            });
            database.Sessions.Add(new WorkspaceSessionDocument
            {
                Id = user + "-session",
                UserId = user,
                Session = new FocusSessionRecord
                {
                    Id = user + "-session"
                }
            });
            database.Conversations.Add(new WorkspaceConversationDocument
            {
                Id = user + "-conversation",
                UserId = user,
                Conversation = new ConversationRecord
                {
                    Id = user + "-conversation",
                    Messages =
                    [
                        new ChatMessageRecord
                        {
                            Text = "Private message"
                        }
                    ]
                }
            });
        }
        await database.SaveChangesAsync();
        var repository = new UserDataResetRepository(database, new ConfigurationBuilder().Build());

        Assert.That(await repository.DeleteWorkspaceProjectsAsync("first"), Is.EqualTo(1));
        Assert.That(await repository.DeleteWorkspaceTasksAsync("first"), Is.EqualTo(1));
        Assert.That(await repository.DeleteWorkspaceSessionsAsync("first"), Is.EqualTo(1));
        Assert.That(await repository.DeleteWorkspaceConversationsAsync("first"), Is.EqualTo(1));
        Assert.That(await repository.DeleteWorkspaceProjectsAsync("first"), Is.Zero);
        Assert.That(await repository.DeleteWorkspaceTasksAsync("first"), Is.Zero);
        Assert.That(await repository.DeleteWorkspaceSessionsAsync("first"), Is.Zero);
        Assert.That(await repository.DeleteWorkspaceConversationsAsync("first"), Is.Zero);

        Assert.That((await database.Projects.SingleAsync()).UserId, Is.EqualTo("second"));
        Assert.That((await database.Tasks.SingleAsync()).UserId, Is.EqualTo("second"));
        Assert.That((await database.Sessions.SingleAsync()).UserId, Is.EqualTo("second"));
        var conversation = await database.Conversations.SingleAsync();
        Assert.That(conversation.UserId, Is.EqualTo("second"));
        Assert.That(conversation.Conversation!.Messages.Single().Text, Is.EqualTo("Private message"));
    }
}
