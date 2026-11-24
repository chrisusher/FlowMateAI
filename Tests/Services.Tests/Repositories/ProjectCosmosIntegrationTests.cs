using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;

namespace Services.Tests.Repositories;

[Explicit("Requires an isolated local Cosmos emulator configured with FLOWMATE_TEST_COSMOS_ENDPOINT.")]
public sealed class ProjectCosmosIntegrationTests
{
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    [Test]
    public async Task ConcurrentCreatesCannotExceedTheActiveProjectLimit()
    {
        var endpoint = Environment.GetEnvironmentVariable("FLOWMATE_TEST_COSMOS_ENDPOINT");

        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !uri.IsLoopback)
        {
            Assert.Fail("Set FLOWMATE_TEST_COSMOS_ENDPOINT to the isolated local emulator.");
        }

        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseCosmos(endpoint!, EmulatorKey, $"flowmate-project-test-{Guid.NewGuid():N}", cosmos => cosmos.ConnectionMode(ConnectionMode.Gateway)).Options;
        await using var setup = new DatabaseContext(options);
        await setup.Database.EnsureCreatedAsync();

        try
        {
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

            var original = await new ProjectWriteRepository(verify).GetAsync("user", "seed-0");

            async Task<ProjectWriteResult> RenameAsync(string name)
            {
                await using var database = new DatabaseContext(options);

                return await new ProjectWriteRepository(database).RenameAsync("user", "seed-0", name, original.Revision!);
            }

            var renames = await Task.WhenAll(RenameAsync("First"), RenameAsync("Second"));
            Assert.That(renames.Count(result => result.Rejection == ProjectChangeRejection.None), Is.EqualTo(1));
            Assert.That(renames.Count(result => result.Rejection == ProjectChangeRejection.Conflict), Is.EqualTo(1));

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
        finally
        {
            try
            {
                await setup.Database.EnsureDeletedAsync();
            }
            catch (CosmosException exception)
            {
                TestContext.Progress.WriteLine($"Emulator database cleanup failed: {exception.StatusCode}. Remove the isolated emulator to clear its data.");
            }
        }
    }
}
