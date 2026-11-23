using System.Text.Json;
using ChrisUsher.Core.Shared;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Workspaces;

public sealed class WorkspacePersistenceTests
{
    [Test]
    public void CosmosModelMapsWorkspaceAndRecordsAsOwnedObjects()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "test").Options;
        using var database = new DatabaseContext(options);
        var workspace = database.Model.FindEntityType(typeof(WorkspaceDocument))!;
        var record = database.Model.FindEntityType(typeof(WorkspaceRecordDocument))!;

        Assert.That(workspace.FindNavigation(nameof(WorkspaceDocument.Workspace))!.TargetEntityType.IsOwned(), Is.True);
        Assert.That(record.FindNavigation(nameof(WorkspaceRecordDocument.Focus))!.TargetEntityType.IsOwned(), Is.True);
        Assert.That(record.FindProperty(nameof(WorkspaceRecordDocument.Payload)), Is.Not.Null);
    }

    [Test]
    public async Task LegacyWorkspaceCanBeReadAndSavedWithNativeData()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var snapshot = new WorkspaceSnapshot { DisplayName = "Test", TimeZone = "UTC", Sessions = [new() { Id = "one", FocusMinutes = 10, PlannedSeconds = 1500 }] };
        await using (var setup = new DatabaseContext(options))
        {
            setup.Workspaces.Add(new WorkspaceDocument { UserId = "user", ETag = "rev", Payload = JsonSerializer.Serialize(snapshot, SharedCommon.JsonOptions) });
            await setup.SaveChangesAsync();
        }

        await using (var database = new DatabaseContext(options))
        {
            var repository = new WorkspaceRepository(database);
            var original = await repository.GetAsync("user");
            Assert.That(original!.Workspace!.DisplayName, Is.EqualTo("Test"));
            var saved = await repository.SaveAsync("user", original.Workspace, original.ETag);
            Assert.That(saved, Is.Not.Null);
        }

        await using (var second = new DatabaseContext(options))
        {
            var repository = new WorkspaceRepository(second);
            var current = await repository.GetAsync("user");
            current!.Workspace!.Sessions[0].PlannedSeconds = 1800;
            await repository.SaveAsync("user", current.Workspace, current.ETag);
        }

        await using (var verify = new DatabaseContext(options))
        {
            var saved = await verify.Workspaces.AsNoTracking().SingleAsync();
            Assert.That(saved.Payload, Is.Null);
            Assert.That(saved.Workspace!.DisplayName, Is.EqualTo("Test"));
            Assert.That(saved.Workspace.Sessions.Single().PlannedSeconds, Is.EqualTo(1800));
            Assert.That((await verify.WorkspaceRecords.AsNoTracking().SingleAsync(x => x.Id == "focus:one")).Focus!.PlannedSeconds, Is.EqualTo(1800));
        }
    }
}
