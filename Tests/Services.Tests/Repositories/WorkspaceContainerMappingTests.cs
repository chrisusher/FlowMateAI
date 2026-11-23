using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Services.Database;

namespace Services.Tests.Repositories;

[TestFixture]
public sealed class WorkspaceContainerMappingTests
{
    [TestCase(typeof(WorkspaceProjectDocument), "Projects")]
    [TestCase(typeof(WorkspaceTaskDocument), "Tasks")]
    [TestCase(typeof(WorkspaceSessionDocument), "Sessions")]
    [TestCase(typeof(WorkspaceConversationDocument), "Conversations")]
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

    [TestCase(typeof(WorkspaceProjectDocument), nameof(WorkspaceProjectDocument.Project), typeof(ProjectRecord))]
    [TestCase(typeof(WorkspaceTaskDocument), nameof(WorkspaceTaskDocument.Task), typeof(TaskRecord))]
    [TestCase(typeof(WorkspaceSessionDocument), nameof(WorkspaceSessionDocument.Session), typeof(FocusSessionRecord))]
    [TestCase(typeof(WorkspaceConversationDocument), nameof(WorkspaceConversationDocument.Conversation), typeof(ConversationRecord))]
    public void WorkspaceItemsUseTypedOwnedRecords(Type documentType, string propertyName, Type recordType)
    {
        using var database = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
            .UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "tests")
            .Options);
        var entity = database.GetService<IDesignTimeModel>().Model.FindEntityType(documentType)!;
        var owned = entity.FindNavigation(propertyName)!.TargetEntityType;

        Assert.That(owned.IsOwned(), Is.True);
        Assert.That(owned.ClrType, Is.EqualTo(recordType));
        Assert.That(entity.FindProperty("Payload"), Is.Null);

        if (recordType == typeof(ConversationRecord))
        {
            Assert.That(owned.FindNavigation(nameof(ConversationRecord.Messages))!.TargetEntityType.IsOwned(), Is.True);
        }
    }
}
