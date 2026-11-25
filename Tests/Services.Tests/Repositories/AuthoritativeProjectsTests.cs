using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

public sealed class AuthoritativeProjectsTests
{
    [Test]
    public async Task SaveAsync_NewEmptyWorkspace_InitialisesProjectStorage()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
        var workspace = new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database));

        var saved = await workspace.SaveAsync("user", new WorkspaceSnapshot(), null);

        Assert.That(saved!.ProjectsAuthoritative, Is.True);
        Assert.That((await database.ProjectIndexes.AsNoTracking().SingleAsync()).ActiveCount, Is.Zero);
        var project = new ProjectRecord
        {
            Id = "project",
            Name = "New project"
        };
        var created = await new ProjectWriteRepository(database).CreateAsync("user", project, BillingPlan.Free);
        Assert.That(created.Rejection, Is.EqualTo(ProjectChangeRejection.None));

        await using var verification = testDatabase.CreateContext();
        var reloaded = await new WorkspaceRepository(verification, new WorkspaceCollectionSynchronizer(verification)).GetAsync("user");
        Assert.That(reloaded!.Workspace!.Projects.Single().Id, Is.EqualTo("project"));
        Assert.That((await verification.ProjectIndexes.AsNoTracking().SingleAsync()).ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetAsync_AuthoritativeProjects_UsesProjectDocuments()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ProjectsAuthoritative = true,
            Workspace = new WorkspaceSnapshot
            {
                Projects = [new ProjectRecord
                {
                    Id = "old",
                    Name = "Old snapshot"
                }]
            }
        });
        database.Projects.AddRange(
            new WorkspaceProjectDocument
            {
                UserId = "user",
                Id = "second",
                Position = 1,
                Project = new ProjectRecord
                {
                    Id = "second",
                    Name = "Second"
                }
            },
            new WorkspaceProjectDocument
            {
                UserId = "user",
                Id = "first",
                Position = 0,
                Project = new ProjectRecord
                {
                    Id = "first",
                    Name = "First"
                }
            },
            new WorkspaceProjectDocument
            {
                UserId = "other",
                Id = "private",
                Project = new ProjectRecord
                {
                    Id = "private",
                    Name = "Other account"
                }
            });
        await database.SaveChangesAsync();

        var synchronizer = new WorkspaceCollectionSynchronizer(database);
        var workspace = new WorkspaceRepository(database, synchronizer);
        var items = new WorkspaceItemsRepository(database);
        var reports = new WorkspaceReportRepository(database);

        Assert.That((await workspace.GetAsync("user"))!.Workspace!.Projects.Select(project => project.Id), Is.EqualTo(new[] { "first", "second" }));
        Assert.That((await items.ListProjectsAsync("user")).Select(project => project.Id), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(await items.GetProjectAsync("user", "old"), Is.Null);
        Assert.That((await reports.GetWorkspaceAsync("user")).Workspace.Projects.Select(project => project.Id), Is.EqualTo(new[] { "first", "second" }));
        var originalRevision = await reports.GetRevisionAsync("user");

        await synchronizer.SynchroniseAsync("user", CancellationToken.None);
        Assert.That((await items.ListProjectsAsync("user")).Select(project => project.Id), Is.EqualTo(new[] { "first", "second" }));

        var first = await database.Projects.SingleAsync(project => project.UserId == "user" && project.Id == "first");
        first.Project!.Name = "Renamed";
        await database.SaveChangesAsync();
        Assert.That(await reports.GetRevisionAsync("user"), Is.Not.EqualTo(originalRevision));
        Assert.That((await reports.GetWorkspaceAsync("user")).Revision, Is.EqualTo(await reports.GetRevisionAsync("user")));
    }
}
