using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Repositories;
using Services.Tests.Doubles;
using Services.Workspaces;

namespace Services.Tests.Repositories;

public sealed class ProjectArchiveRepositoryTests
{
    [Test]
    public async Task ArchiveClearsIdleSelectionAndPreservesRelatedRecords()
    {
        await using var database = await CreateDatabaseAsync();
        var repository = new ProjectArchiveRepository(database);

        var result = await repository.ArchiveAsync("user", "project", "project-revision", "workspace-revision");

        Assert.That(result.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(result.Project!.IsArchived, Is.True);
        Assert.That(result.Timer!.TaskId, Is.Null);
        Assert.That(result.Timer.ProjectId, Is.Empty);
        var workspace = await database.Workspaces.AsNoTracking().SingleAsync();
        Assert.That(workspace.PendingProjectArchive, Is.Null);
        Assert.That(workspace.Workspace!.Tasks.Single().ProjectId, Is.EqualTo("project"));
        Assert.That(workspace.Workspace.Sessions.Single().ProjectId, Is.EqualTo("project"));
        Assert.That((await database.ProjectIndexes.AsNoTracking().SingleAsync()).ActiveCount, Is.Zero);
    }

    [Test]
    public async Task ActiveTimerPreventsArchiveWithoutLeavingAGuard()
    {
        await using var database = await CreateDatabaseAsync();
        var workspace = await database.Workspaces.SingleAsync();
        workspace.Workspace!.Timer.Phase = TimerPhase.Focus;
        await database.SaveChangesAsync();

        var result = await new ProjectArchiveRepository(database).ArchiveAsync("user", "project", "project-revision", "workspace-revision");

        Assert.That(result.Rejection, Is.EqualTo(ProjectChangeRejection.TimerActive));
        Assert.That((await database.Workspaces.AsNoTracking().SingleAsync()).PendingProjectArchive, Is.Null);
        Assert.That((await database.Projects.AsNoTracking().SingleAsync()).Project!.IsArchived, Is.False);
        Assert.That((await database.ProjectIndexes.AsNoTracking().SingleAsync()).ActiveCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StaleWorkspaceRevisionCannotStartArchive()
    {
        await using var database = await CreateDatabaseAsync();
        var result = await new ProjectArchiveRepository(database).ArchiveAsync("user", "project", "project-revision", "stale-workspace");

        Assert.That(result.Rejection, Is.EqualTo(ProjectChangeRejection.Conflict));
        Assert.That((await database.Workspaces.AsNoTracking().SingleAsync()).PendingProjectArchive, Is.Null);
        Assert.That((await database.Projects.AsNoTracking().SingleAsync()).Project!.IsArchived, Is.False);
    }

    [Test]
    public async Task WorkspaceSaveCannotPassAnExistingArchiveGuard()
    {
        await using var database = await CreateDatabaseAsync();
        var workspace = await database.Workspaces.SingleAsync();
        workspace.PendingProjectArchive = new ProjectArchiveOperation
        {
            ProjectId = "project",
            ProjectRevision = "project-revision"
        };
        await database.SaveChangesAsync();
        var repository = new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database));

        var saved = await repository.SaveAsync("user", new WorkspaceSnapshot(), "workspace-revision");

        Assert.That(saved, Is.Null);
        Assert.That((await database.Workspaces.AsNoTracking().SingleAsync()).PendingProjectArchive, Is.Not.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RecoveryCompletesAnInterruptedArchiveOnlyOnce(bool alreadyArchived)
    {
        await using var database = await CreateDatabaseAsync();
        var workspace = await database.Workspaces.SingleAsync();
        workspace.PendingProjectArchive = new ProjectArchiveOperation
        {
            ProjectId = "project",
            ProjectRevision = "project-revision"
        };

        if (alreadyArchived)
        {
            var project = await database.Projects.SingleAsync();
            project.Project!.IsArchived = true;
            project.ArchiveOriginRevision = "project-revision";
            (await database.ProjectIndexes.SingleAsync()).ActiveCount = 0;
        }

        await database.SaveChangesAsync();
        var repository = new ProjectArchiveRepository(database);
        var recovered = await repository.RecoverAsync("user");

        Assert.That(recovered!.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(await repository.RecoverAsync("user"), Is.Null);
        Assert.That((await database.ProjectIndexes.AsNoTracking().SingleAsync()).ActiveCount, Is.Zero);
        Assert.That((await database.Workspaces.AsNoTracking().SingleAsync()).PendingProjectArchive, Is.Null);
    }

    [Test]
    public async Task WorkspaceReadRecoversAnInterruptedArchive()
    {
        await using var database = await CreateDatabaseAsync();
        var workspace = await database.Workspaces.SingleAsync();
        workspace.PendingProjectArchive = new ProjectArchiveOperation
        {
            ProjectId = "project",
            ProjectRevision = "project-revision"
        };
        await database.SaveChangesAsync();
        var service = new WorkspaceService(new WorkspaceRepository(database, new WorkspaceCollectionSynchronizer(database)),
            new EmptyArchive(), new StubBillingRepository(), new ProjectArchiveRepository(database));

        var result = await service.GetAsync("user");

        Assert.That(result.Workspace.Projects.Single().IsArchived, Is.True);
        Assert.That(result.Workspace.Timer.TaskId, Is.Null);
        Assert.That((await database.Workspaces.AsNoTracking().SingleAsync()).PendingProjectArchive, Is.Null);
    }

    private sealed class EmptyArchive : IActivityArchiveRepository
    {
        public Task ArchiveAsync(string ownerId, IReadOnlyCollection<FocusSessionRecord> sessions, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<DatabaseContext> CreateDatabaseAsync()
    {
        var database = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        database.Workspaces.Add(new WorkspaceDocument
        {
            UserId = "user",
            ETag = "workspace-revision",
            ProjectsAuthoritative = true,
            Workspace = new WorkspaceSnapshot
            {
                Timer = new TimerSnapshot
                {
                    Phase = TimerPhase.Idle,
                    ProjectId = "project",
                    TaskId = "task"
                },
                Tasks = [new TaskRecord
                {
                    Id = "task",
                    ProjectId = "project"
                }],
                Sessions = [new FocusSessionRecord
                {
                    Id = "session",
                    ProjectId = "project"
                }]
            }
        });
        database.Projects.Add(new WorkspaceProjectDocument
        {
            UserId = "user",
            Id = "project",
            ETag = "project-revision",
            Project = new ProjectRecord
            {
                Id = "project",
                Name = "Project"
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

        return database;
    }
}
