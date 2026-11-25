using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;
using Shared.Enums;
using Shared.Models;

namespace Services.Tests.Repositories;

public sealed class ProjectWriteRepositoryTests
{
    [Test]
    public async Task Query_ProjectAndIndex_TranslatesForCosmos()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();

        Assert.DoesNotThrow(() => database.ProjectIndexes.Where(item => item.UserId == "user" && item.Id == "project-index").ToQueryString());
        Assert.DoesNotThrow(() => database.Projects.Where(item => item.UserId == "user" && item.Id == "project").ToQueryString());
    }

    [Test]
    public async Task CreateAsync_RetriedRequest_ReservesOneSlotAndRecord()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
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

        for (var position = 0; position < 4; position++)
        {
            database.Projects.Add(new WorkspaceProjectDocument
            {
                UserId = "user",
                Id = $"existing-{position}",
                Position = position,
                Project = new ProjectRecord
                {
                    Id = $"existing-{position}",
                    Name = $"Existing {position}",
                    IsArchived = position >= 2
                }
            });
        }
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
        Assert.That(await database.Projects.CountAsync(), Is.EqualTo(5));
        Assert.That((await database.Projects.AsNoTracking().SingleAsync(item => item.Id == "new-project")).Position, Is.EqualTo(4));
        var index = await database.ProjectIndexes.AsNoTracking().SingleAsync();
        Assert.That(index.ActiveCount, Is.EqualTo(3));
        Assert.That(index.TotalCount, Is.EqualTo(5));
        Assert.That(index.NextPosition, Is.EqualTo(5));

        var rejected = await repository.CreateAsync("user", new ProjectRecord
        {
            Id = "over-limit",
            Name = "Over limit"
        }, BillingPlan.Free);
        Assert.That(rejected.Rejection, Is.EqualTo(ProjectChangeRejection.ProjectLimit));
        Assert.That(await database.Projects.CountAsync(), Is.EqualTo(5));
    }

    [Test]
    public async Task RenameAsync_StaleRevisionOrOtherOwner_RejectsChange()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
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
            Project = new ProjectRecord
            {
                Id = "project",
                Name = "Before"
            }
        });
        database.ProjectIndexes.Add(new ProjectIndexDocument
        {
            UserId = "user",
            ActiveCount = 1,
            TotalCount = 1,
            NextPosition = 1
        });
        await database.SaveChangesAsync();
        var revision = (await database.Projects.AsNoTracking().SingleAsync()).ETag!;
        var repository = new ProjectWriteRepository(database);

        Assert.That((await repository.RenameAsync("other", "project", "Other", revision)).Rejection, Is.EqualTo(ProjectChangeRejection.StorageNotReady));
        Assert.That((await repository.RenameAsync("user", "project", "Stale", "old")).Rejection, Is.EqualTo(ProjectChangeRejection.Conflict));
        Assert.That((await repository.RenameAsync("user", "project", "After", revision)).Rejection, Is.EqualTo(ProjectChangeRejection.None));
        await using var verification = testDatabase.CreateContext();
        Assert.That((await verification.Projects.AsNoTracking().SingleAsync()).Project!.Name, Is.EqualTo("After"));
    }

    [Test]
    public async Task RestoreAsync_StaleOrOverLimitRequest_RejectsChange()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
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
            Position = 3,
            Project = new ProjectRecord
            {
                Id = "archived",
                IsArchived = true
            }
        });

        for (var position = 0; position < 3; position++)
        {
            database.Projects.Add(new WorkspaceProjectDocument
            {
                UserId = "user",
                Id = $"active-{position}",
                Position = position,
                Project = new ProjectRecord
                {
                    Id = $"active-{position}",
                    Name = $"Active {position}"
                }
            });
        }
        await database.SaveChangesAsync();
        var revision = (await database.Projects.AsNoTracking().SingleAsync(item => item.Id == "archived")).ETag!;
        var repository = new ProjectWriteRepository(database);

        Assert.That((await repository.RestoreAsync("user", "archived", "stale", BillingPlan.Free)).Rejection, Is.EqualTo(ProjectChangeRejection.Conflict));
        Assert.That((await repository.RestoreAsync("user", "archived", revision, BillingPlan.Free)).Rejection, Is.EqualTo(ProjectChangeRejection.ProjectLimit));
        Assert.That((await database.Projects.AsNoTracking().SingleAsync(item => item.Id == "archived")).Project!.IsArchived, Is.True);
        var restored = await repository.RestoreAsync("user", "archived", revision, BillingPlan.Pro);
        Assert.That(restored.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(restored.Project!.IsArchived, Is.False);
        await using var verification = testDatabase.CreateContext();
        Assert.That((await verification.ProjectIndexes.AsNoTracking().SingleAsync()).ActiveCount, Is.EqualTo(4));
    }
}
