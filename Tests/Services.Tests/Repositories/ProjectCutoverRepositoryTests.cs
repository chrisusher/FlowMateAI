using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

public sealed class ProjectCutoverRepositoryTests
{
    [Test]
    public async Task ApplyAsync_VerifiedLegacyProjects_PreservesRecordsAndSetsIndex()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ETag = "revision",
            Workspace = new WorkspaceSnapshot
            {
                Projects = [
                    new ProjectRecord
                    {
                        Id = "project-index",
                        Name = "First"
                    },
                    new ProjectRecord
                    {
                        Id = "second",
                        Name = "Archived",
                        IsArchived = true
                    }
                ]
            }
        });
        await database.SaveChangesAsync();
        var cutover = new ProjectCutoverRepository(database);

        var preview = await cutover.PreviewAsync("user");
        Assert.That(preview.ProjectCount, Is.EqualTo(2));
        Assert.That(preview.ActiveCount, Is.EqualTo(1));
        Assert.That(await database.Projects.CountAsync(), Is.Zero);
        Assert.That(await database.ProjectIndexes.CountAsync(), Is.Zero);

        await cutover.ApplyAsync("user", preview.Revision);
        var stored = await database.Workspaces.AsNoTracking().SingleAsync();
        var index = await database.ProjectIndexes.AsNoTracking().SingleAsync();
        Assert.That(stored.ProjectsAuthoritative, Is.True);
        Assert.That(stored.Workspace!.Projects.Select(project => project.Id), Is.EqualTo(new[] { "project-index", "second" }));
        Assert.That(index.ActiveCount, Is.EqualTo(1));
        Assert.That(index.TotalCount, Is.EqualTo(2));
        Assert.That(index.Id, Is.EqualTo(stored.ProjectIndexId));
        Assert.That(index.Id, Is.Not.EqualTo("project-index"));
        Assert.That((await database.Projects.AsNoTracking().OrderBy(project => project.Position).ToListAsync()).Select(project => project.Id), Is.EqualTo(new[] { "project-index", "second" }));
    }

    [Test]
    public async Task ApplyAsync_ConflictingDestinationOrRevision_RejectsWithoutWriting()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ETag = "revision",
            Workspace = new WorkspaceSnapshot
            {
                Projects = [new ProjectRecord
                {
                    Id = "first",
                    Name = "Source"
                }]
            }
        });
        await database.SaveChangesAsync();
        var cutover = new ProjectCutoverRepository(database);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cutover.ApplyAsync("user", "stale"));
        Assert.That(await database.ProjectIndexes.CountAsync(), Is.Zero);

        database.Projects.Add(new WorkspaceProjectDocument
        {
            UserId = "user",
            Id = "first",
            Project = new ProjectRecord
            {
                Id = "first",
                Name = "Different"
            }
        });
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cutover.PreviewAsync("user"));
        Assert.That((await database.Workspaces.SingleAsync()).ProjectsAuthoritative, Is.False);
    }

    private static DatabaseContext CreateDatabase() => new(new DbContextOptionsBuilder<DatabaseContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
