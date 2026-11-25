using Services.Database;
using Services.Repositories;
using Services.Tests.Doubles;
using Services.Workspaces;

namespace Services.Tests.Workspaces;

public sealed class ProjectWriteServiceTests
{
    [Test]
    public async Task CreateAsync_ProjectName_ValidatesAndUsesAccountPlan()
    {
        var repository = new RecordingRepository();
        var billing = new StubBillingRepository
        {
            Entitlement = new BillingEntitlementDocument
            {
                UserId = "user",
                Plan = "pro"
            }
        };
        var service = new ProjectWriteService(repository, billing);
        var invalid = await service.CreateAsync("user", new ProjectRecord
        {
            Id = "invalid",
            Name = "  "
        });
        Assert.That(invalid.Rejection, Is.EqualTo(ProjectChangeRejection.InvalidInput));
        Assert.That(repository.Calls, Is.Zero);

        var valid = await service.CreateAsync("user", new ProjectRecord
        {
            Id = "project",
            Name = "  Good project  ",
            Color = "#112233"
        });
        Assert.That(valid.Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(repository.Name, Is.EqualTo("Good project"));
        Assert.That(repository.Owner, Is.EqualTo("user"));
        Assert.That(repository.Plan, Is.EqualTo(BillingPlan.Pro));
    }

    [Test]
    public async Task RenameAsync_MissingRevisionOrInvalidName_RejectsChange()
    {
        var repository = new RecordingRepository();
        var service = new ProjectWriteService(repository, new StubBillingRepository());

        Assert.That((await service.RenameAsync("user", "project", "Name", "")).Rejection, Is.EqualTo(ProjectChangeRejection.InvalidInput));
        Assert.That((await service.RenameAsync("user", "project", "Bad\nname", "revision")).Rejection, Is.EqualTo(ProjectChangeRejection.InvalidInput));
        Assert.That(repository.Calls, Is.Zero);
        Assert.That((await service.RenameAsync("user", "project", " Name ", "revision")).Rejection, Is.EqualTo(ProjectChangeRejection.None));
        Assert.That(repository.Name, Is.EqualTo("Name"));
    }

    private sealed class RecordingRepository : IProjectWriteRepository
    {
        public int Calls { get; private set; }
        public string? Name { get; private set; }
        public string? Owner { get; private set; }
        public BillingPlan Plan { get; private set; }

        public Task<ProjectWriteResult> GetAsync(string userId, string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectWriteResult(null, null, ProjectChangeRejection.NotFound));

        public Task<ProjectWriteResult> RestoreAsync(string userId, string id, string revision, BillingPlan plan, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectWriteResult(null, null, ProjectChangeRejection.NotFound));

        public Task<ProjectWriteResult> CreateAsync(string userId, ProjectRecord project, BillingPlan plan, CancellationToken cancellationToken = default)
        {
            Calls++;
            Owner = userId;
            Name = project.Name;
            Plan = plan;

            return Task.FromResult(new ProjectWriteResult(project, "revision", ProjectChangeRejection.None));
        }

        public Task<ProjectWriteResult> RenameAsync(string userId, string id, string name, string revision, CancellationToken cancellationToken = default)
        {
            Calls++;
            Name = name;

            return Task.FromResult(new ProjectWriteResult(new ProjectRecord
            {
                Id = id,
                Name = name
            }, "revision", ProjectChangeRejection.None));
        }
    }
}
