using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Services.Database;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class WorkspaceContainerMappingTests
{
    [TestCase(typeof(WorkspaceProjectDocument), "WorkspaceProjects")]
    [TestCase(typeof(WorkspaceTaskDocument), "WorkspaceTasks")]
    [TestCase(typeof(WorkspaceSessionDocument), "WorkspaceSessions")]
    [TestCase(typeof(WorkspaceConversationDocument), "WorkspaceConversations")]
    public void WorkspaceItemsUseSeparateUserPartitionedContainers(Type documentType, string container)
    {
        using var database = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
            .UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "tests")
            .Options);
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(documentType)!;

        Assert.That(entity.GetContainer(), Is.EqualTo(container));
        Assert.That(entity.GetPartitionKeyPropertyNames(), Is.EqualTo(new[] { nameof(IWorkspaceItemDocument.UserId) }));
        Assert.That(entity.FindPrimaryKey()!.Properties.Select(property => property.Name), Is.EqualTo(new[] { nameof(IWorkspaceItemDocument.Id) }));
        Assert.That(entity.FindProperty(nameof(IWorkspaceItemDocument.ETag))!.IsConcurrencyToken, Is.True);
    }
}
