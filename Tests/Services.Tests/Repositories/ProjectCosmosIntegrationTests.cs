using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

public sealed class ProjectCosmosIntegrationTests
{
    [Test]
    public async Task AuthoritativeProjectReads_DefaultCosmosIndex_ReturnOrderedOwnerProjects()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
        var workspaces = new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database));
        await workspaces.SaveAsync("user", new WorkspaceSnapshot(), null);
        foreach (var (user, id, position) in new[] { ("user", "b", 1), ("user", "a", 1), ("user", "c", 0), ("other", "x", 0) })
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
        database.ChangeTracker.Clear();

        var workspace = await workspaces.GetAsync("user");
        var projects = await new WorkspaceItemsRepository(database).ListProjectsAsync("user");
        var report = await new WorkspaceReportRepository(database).GetWorkspaceAsync("user");
        var expected = new[] { "c", "a", "b" };
        Assert.That(workspace!.Workspace!.Projects.Select(project => project.Id), Is.EqualTo(expected));
        Assert.That(projects.Select(project => project.Id), Is.EqualTo(expected));
        Assert.That(report.Workspace.Projects.Select(project => project.Id), Is.EqualTo(expected));
    }

    [Test]
    public async Task CreateAsync_ConcurrentProjectChanges_EnforcesLimitsAndPreservesRecords()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        var options = testDatabase.CreateOptions();
        await using var setup = new DatabaseContext(options);
        await new WorkspaceRepository(setup, new WorkspaceCollectionSynchronizer(setup)).SaveAsync("user", new WorkspaceSnapshot(), null);

        for (var index = 0; index < 2; index++)
        {
            await using var seed = new DatabaseContext(options);
            var result = await new ProjectWriteRepository(seed).CreateAsync("user", new ProjectRecord
            {
                Id = $"seed-{index}",
                Name = $"Seed {index}"
            }, BillingPlan.Free);
            Assert.That(result.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        }

        async Task<ProjectWriteResult> CreateAsync(string id)
        {
            await using var database = new DatabaseContext(options);

            return await new ProjectWriteRepository(database).CreateAsync("user", new ProjectRecord
            {
                Id = id,
                Name = id
            }, BillingPlan.Free);
        }

        var results = await Task.WhenAll(CreateAsync("candidate-a"), CreateAsync("candidate-b"));
        Assert.That(results.Count(result => result.Rejection == ProjectChangeRejection.None), Is.EqualTo(1));
        Assert.That(results.Count(result => result.Rejection is ProjectChangeRejection.Conflict or ProjectChangeRejection.ProjectLimit), Is.EqualTo(1));
        await using var verify = new DatabaseContext(options);
        Assert.That(await verify.Projects.CountAsync(project => project.UserId == "user"), Is.EqualTo(3));
        Assert.That((await verify.ProjectIndexes.SingleAsync(index => index.UserId == "user")).ActiveCount, Is.EqualTo(3));
        var reloaded = await new WorkspaceRepository(verify, new WorkspaceCollectionSynchronizer(verify)).GetAsync("user");
        Assert.That(reloaded!.Workspace!.Projects.Select(project => project.Id).Take(2), Is.EqualTo(new[] { "seed-0", "seed-1" }));
        Assert.That(reloaded.Workspace.Projects.Count, Is.EqualTo(3));

        var original = await new ProjectWriteRepository(verify).GetAsync("user", "seed-0");

        async Task<ProjectWriteResult> RenameAsync(string name)
        {
            await using var database = new DatabaseContext(options);

            return await new ProjectWriteRepository(database).RenameAsync("user", "seed-0", name, original.Revision!);
        }

        var renames = await Task.WhenAll(RenameAsync("First"), RenameAsync("Second"));
        Assert.That(renames.Count(result => result.Rejection == ProjectChangeRejection.None), Is.EqualTo(1));
        Assert.That(renames.Count(result => result.Rejection == ProjectChangeRejection.Conflict), Is.EqualTo(1));

        await using var revisions = new DatabaseContext(options);
        var firstCurrent = await new ProjectWriteRepository(revisions).GetAsync("user", "seed-0");
        var secondCurrent = await new ProjectWriteRepository(revisions).GetAsync("user", "seed-1");

        async Task<ProjectWriteResult> RenameDifferentAsync(string id, string revision)
        {
            await using var database = new DatabaseContext(options);

            return await new ProjectWriteRepository(database).RenameAsync("user", id, $"Renamed {id}", revision);
        }

        var independent = await Task.WhenAll(RenameDifferentAsync("seed-0", firstCurrent.Revision!), RenameDifferentAsync("seed-1", secondCurrent.Revision!));
        Assert.That(independent.All(result => result.Rejection == ProjectChangeRejection.None), Is.True);

        await using var interrupted = new DatabaseContext(options);
        var project = await new ProjectWriteRepository(interrupted).GetAsync("user", "seed-0");
        var document = await interrupted.Workspaces.SingleAsync(workspace => workspace.UserId == "user" && workspace.Id == "workspace");
        document.PendingProjectArchive = new ProjectArchiveOperation
        {
            ProjectId = "seed-0",
            ProjectRevision = project.Revision!
        };
        await interrupted.SaveChangesAsync();
        var blocked = await new WorkspaceRepository(interrupted, new WorkspaceCollectionSynchronizer(interrupted))
            .SaveAsync("user", new WorkspaceSnapshot(), document.ETag);
        Assert.That(blocked, Is.Null);
        var recovered = await new ProjectArchiveRepository(interrupted).RecoverAsync("user");
        Assert.That(recovered!.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(recovered.Project!.IsArchived, Is.True);
        Assert.That((await interrupted.ProjectIndexes.AsNoTracking().SingleAsync(index => index.UserId == "user")).ActiveCount, Is.EqualTo(2));

        await using var restore = new DatabaseContext(options);
        var restored = await new ProjectWriteRepository(restore).RestoreAsync("user", "seed-0", recovered.Revision!, BillingPlan.Free);
        Assert.That(restored.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That((await restore.ProjectIndexes.AsNoTracking().SingleAsync(index => index.UserId == "user")).ActiveCount, Is.EqualTo(3));

        await using var archive = new DatabaseContext(options);
        var toArchive = await new ProjectWriteRepository(archive).GetAsync("user", "seed-1");
        var currentWorkspace = await archive.Workspaces.AsNoTracking().SingleAsync(workspace => workspace.UserId == "user" && workspace.Id == "workspace");
        var archived = await new ProjectArchiveRepository(archive).ArchiveAsync("user", "seed-1", toArchive.Revision!, currentWorkspace.ETag!);
        Assert.That(archived.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(archived.Project!.IsArchived, Is.True);
        Assert.That((await archive.ProjectIndexes.AsNoTracking().SingleAsync(index => index.UserId == "user")).ActiveCount, Is.EqualTo(2));

        async Task<ProjectWriteResult> RestoreCandidateAsync()
        {
            await using var database = new DatabaseContext(options);

            return await new ProjectWriteRepository(database).RestoreAsync("user", "seed-1", archived.Revision!, BillingPlan.Free);
        }

        var slotRace = await Task.WhenAll(RestoreCandidateAsync(), CreateAsync("candidate-restore-race"));
        Assert.That(slotRace.Count(result => result.Rejection == ProjectChangeRejection.None), Is.EqualTo(1));
        Assert.That(slotRace.Count(result => result.Rejection is ProjectChangeRejection.Conflict or ProjectChangeRejection.ProjectLimit), Is.EqualTo(1));
        await using var final = new DatabaseContext(options);
        Assert.That((await final.ProjectIndexes.AsNoTracking().SingleAsync(index => index.UserId == "user")).ActiveCount, Is.EqualTo(3));

        await using var legacy = new DatabaseContext(options);
        var legacyWorkspace = new WorkspaceSnapshot
        {
            Projects = [new ProjectRecord
            {
                Id = "project-index",
                Name = "Legacy project"
            }]
        };
        var writer = new WorkspaceRepository(legacy, new WorkspaceCollectionSynchronizer(legacy));
        await writer.SaveAsync("legacy-user", legacyWorkspace, null);
        var cutover = new ProjectCutoverRepository(legacy);
        var preview = await cutover.PreviewAsync("legacy-user");
        await cutover.ApplyAsync("legacy-user", preview.Revision);
        var migrated = await writer.GetAsync("legacy-user");
        Assert.That(migrated!.ProjectsAuthoritative, Is.True);
        Assert.That(migrated.Workspace!.Projects.Single().Id, Is.EqualTo("project-index"));
        Assert.That(migrated.ProjectIndexId, Is.Not.EqualTo("project-index"));
    }
}
