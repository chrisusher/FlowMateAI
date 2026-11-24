using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;
using Shared.Enums;
using Shared.Models;

namespace Services.Tests.Repositories;

public sealed class ProjectWriteRepositoryTests
{
    [Test]
    public void Query_ProjectAndIndex_TranslatesForCosmos()
    {
        using var database = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
            .UseCosmos("https://localhost:8081", Convert.ToBase64String(new byte[64]), "tests").Options);

        Assert.DoesNotThrow(() => database.ProjectIndexes.Where(item => item.UserId == "user" && item.Id == "project-index").ToQueryString());
        Assert.DoesNotThrow(() => database.Projects.Where(item => item.UserId == "user" && item.Id == "project").ToQueryString());
    }

    [Test]
    public async Task CreateAsync_RetriedRequest_ReservesOneSlotAndRecord()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ProjectsAuthoritative = true,
            Workspace = new WorkspaceSnapshot()
        });
        database.ProjectIndexes.Add(new ProjectIndexDocument
        {
            UserId = "user",
            ActiveCount = 2,
            TotalCount = 4,
            NextPosition = 4
        });
        await database.SaveChangesAsync();
        var repository = new ProjectWriteRepository(database);
        var project = new ProjectRecord
        {
            Id = "new-project",
            Name = "New"
        };

        var created = await repository.CreateAsync("user", project, BillingPlan.Free);
        var retried = await repository.CreateAsync("user", project, BillingPlan.Free);

        Assert.That(created.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(retried.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(await database.Projects.CountAsync(), Is.EqualTo(1));
        Assert.That((await database.Projects.SingleAsync()).Position, Is.EqualTo(4));
        var index = await database.ProjectIndexes.SingleAsync();
        Assert.That(index.ActiveCount, Is.EqualTo(3));
        Assert.That(index.TotalCount, Is.EqualTo(5));
        Assert.That(index.NextPosition, Is.EqualTo(5));

        var rejected = await repository.CreateAsync("user", new ProjectRecord
        {
            Id = "over-limit",
            Name = "Over limit"
        }, BillingPlan.Free);
        Assert.That(rejected.Rejection, Is.EqualTo(ProjectChangeRejection.ProjectLimit));
        Assert.That(await database.Projects.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task RenameAsync_StaleRevisionOrOtherOwner_RejectsChange()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ProjectsAuthoritative = true,
            Workspace = new WorkspaceSnapshot()
        });
        database.Projects.Add(new WorkspaceProjectDocument
        {
            UserId = "user",
            Id = "project",
            ETag = "revision-1",
            Project = new ProjectRecord
            {
                Id = "project",
                Name = "Before"
            }
        });
        await database.SaveChangesAsync();
        var repository = new ProjectWriteRepository(database);

        Assert.That((await repository.RenameAsync("other", "project", "Other", "revision-1")).Rejection, Is.EqualTo(ProjectChangeRejection.StorageNotReady));
        Assert.That((await repository.RenameAsync("user", "project", "Stale", "old")).Rejection, Is.EqualTo(ProjectChangeRejection.Conflict));
        Assert.That((await repository.RenameAsync("user", "project", "After", "revision-1")).Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That((await database.Projects.SingleAsync()).Project!.Name, Is.EqualTo("After"));
    }

    [Test]
    public async Task RestoreAsync_StaleOrOverLimitRequest_RejectsChange()
    {
        await using var database = CreateDatabase();
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ProjectsAuthoritative = true,
            Workspace = new WorkspaceSnapshot()
        });
        database.ProjectIndexes.Add(new ProjectIndexDocument
        {
            UserId = "user",
            ActiveCount = 3,
            TotalCount = 4,
            NextPosition = 4
        });
        database.Projects.Add(new WorkspaceProjectDocument
        {
            UserId = "user",
            Id = "archived",
            ETag = "revision",
            Project = new ProjectRecord
            {
                Id = "archived",
                IsArchived = true
            }
        });
        await database.SaveChangesAsync();
        var repository = new ProjectWriteRepository(database);

        Assert.That((await repository.RestoreAsync("user", "archived", "stale", BillingPlan.Free)).Rejection, Is.EqualTo(ProjectChangeRejection.Conflict));
        Assert.That((await repository.RestoreAsync("user", "archived", "revision", BillingPlan.Free)).Rejection, Is.EqualTo(ProjectChangeRejection.ProjectLimit));
        Assert.That((await database.Projects.SingleAsync()).Project!.IsArchived, Is.True);
        var restored = await repository.RestoreAsync("user", "archived", "revision", BillingPlan.Pro);
        Assert.That(restored.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(restored.Project!.IsArchived, Is.False);
        Assert.That((await database.ProjectIndexes.SingleAsync()).ActiveCount, Is.EqualTo(4));
    }

    private static DatabaseContext CreateDatabase() => new(new DbContextOptionsBuilder<DatabaseContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
}
