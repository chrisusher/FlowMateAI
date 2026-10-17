using System.Text.Json;
using ChrisUsher.Core.Shared;
using Services.Database;
using Services.Repositories;
using Services.Tests.Doubles;
using Services.Workspaces;

namespace Services.Tests.Workspaces;

public sealed class WorkspaceServiceTests
{
    [Test]
    public async Task ConcurrentClientsCannotOverwriteAnAcceptedWorkspaceRevision()
    {
        var repository = new InMemoryWorkspaceRepository();
        repository.Seed("user", new WorkspaceSnapshot { DisplayName = "Initial" }, "revision-1");
        var service = CreateService(repository);

        var first = await service.SaveAsync("user", new(new WorkspaceSnapshot { DisplayName = "First device" }, "revision-1"));
        var stale = await service.SaveAsync("user", new(new WorkspaceSnapshot { DisplayName = "Second device" }, "revision-1"));

        Assert.That(first.Succeeded, Is.True);
        Assert.That(first.Response!.Revision, Is.EqualTo("revision-2"));
        Assert.That(stale.ErrorCode, Is.EqualTo(WorkspaceSaveErrorCode.RevisionConflict));
        Assert.That(repository.Read("user").DisplayName, Is.EqualTo("First device"));
    }

    [Test]
    public async Task ExistingWorkspaceRejectsAClientThatOmitsItsRevision()
    {
        var repository = new InMemoryWorkspaceRepository();
        repository.Seed("user", new WorkspaceSnapshot { DisplayName = "Initial" }, "revision-1");
        var service = CreateService(repository);

        var result = await service.SaveAsync("user", new(new WorkspaceSnapshot { DisplayName = "Unversioned write" }, null));

        Assert.That(result.ErrorCode, Is.EqualTo(WorkspaceSaveErrorCode.RevisionConflict));
        Assert.That(repository.Read("user").DisplayName, Is.EqualTo("Initial"));
        Assert.That(repository.SaveCount, Is.Zero);
    }

    [Test]
    public async Task TimerOwnedByAnotherClientIsPreservedWhenConflictingSaveIsRejected()
    {
        var current = new WorkspaceSnapshot
        {
            ClientId = "device-a",
            Timer = new()
            {
                Phase = TimerPhase.Focus,
                OwnerClientId = "device-a",
                StartedAt = DateTimeOffset.UtcNow,
                EndsAt = DateTimeOffset.UtcNow.AddMinutes(20),
                DurationSeconds = 1200
            }
        };
        var repository = new InMemoryWorkspaceRepository();
        repository.Seed("user", current, "revision-1");
        var service = CreateService(repository);
        var otherDevice = new WorkspaceSnapshot { ClientId = "device-b" };

        var result = await service.SaveAsync("user", new(otherDevice, "revision-1"));

        Assert.That(result.ErrorCode, Is.EqualTo(WorkspaceSaveErrorCode.TimerConflict));
        Assert.That(repository.Read("user").Timer.OwnerClientId, Is.EqualTo("device-a"));
        Assert.That(repository.SaveCount, Is.Zero);
    }

    [Test]
    public async Task ExplicitReclaimTransfersAnUnchangedActiveTimerToTheRequestingClient()
    {
        var timer = new TimerSnapshot
        {
            Phase = TimerPhase.Paused,
            OwnerClientId = "closed-device",
            RemainingSeconds = 900,
            DurationSeconds = 1500,
            FocusSessionId = "focus-session"
        };
        var current = new WorkspaceSnapshot { ClientId = "closed-device", Timer = timer };
        var repository = new InMemoryWorkspaceRepository();
        repository.Seed("user", current, "revision-1");
        var service = CreateService(repository);
        var reclaimed = new WorkspaceSnapshot
        {
            ClientId = "reopened-device",
            Timer = new TimerSnapshot
            {
                Phase = timer.Phase,
                OwnerClientId = "reopened-device",
                RemainingSeconds = timer.RemainingSeconds,
                DurationSeconds = timer.DurationSeconds,
                FocusSessionId = timer.FocusSessionId
            }
        };

        var result = await service.SaveAsync("user", new(reclaimed, "revision-1", ReclaimTimer: true));

        Assert.That(result.Succeeded, Is.True);
        Assert.That(repository.Read("user").Timer.OwnerClientId, Is.EqualTo("reopened-device"));
    }

    [Test]
    public async Task ArchivingAProjectAllowsAReplacementWithoutRemovingItsHistory()
    {
        var current = new WorkspaceSnapshot
        {
            Projects =
            [
                new() { Id = "one", Name = "One" },
                new() { Id = "two", Name = "Two" },
                new() { Id = "three", Name = "Three" }
            ],
            Tasks = [new() { Id = "task-one", ProjectId = "one", Title = "Keep this task" }],
            Sessions = [new() { Id = "session-one", ProjectId = "one", FocusMinutes = 12 }]
        };
        var repository = new InMemoryWorkspaceRepository();
        repository.Seed("user", current, "revision-1");
        var service = CreateService(repository);
        current.Projects[0].IsArchived = true;
        current.Projects.Add(new() { Id = "four", Name = "Four" });

        var result = await service.SaveAsync("user", new(current, "revision-1"));

        Assert.That(result.Succeeded, Is.True);
        var saved = repository.Read("user");
        Assert.That(saved.Projects.Count(project => !project.IsArchived), Is.EqualTo(3));
        Assert.That(saved.Projects.Single(project => project.Id == "one").IsArchived, Is.True);
        Assert.That(saved.Tasks.Single().ProjectId, Is.EqualTo("one"));
        Assert.That(saved.Sessions.Single().ProjectId, Is.EqualTo("one"));
    }

    [Test]
    public async Task ServerRejectsAddingAnActiveProjectBeyondTheLimit()
    {
        var repository = new InMemoryWorkspaceRepository();
        repository.Seed("user", new WorkspaceSnapshot
        {
            Projects =
            [
                new() { Id = "one", Name = "One" },
                new() { Id = "two", Name = "Two" },
                new() { Id = "three", Name = "Three" }
            ]
        }, "revision-1");
        var request = repository.Read("user");
        request.Projects.Add(new() { Id = "four", Name = "Four" });

        var result = await CreateService(repository).SaveAsync("user", new(request, "revision-1"));

        Assert.That(result.ErrorCode, Is.EqualTo(WorkspaceSaveErrorCode.ProjectLimit));
        Assert.That(repository.SaveCount, Is.Zero);
    }

    [Test]
    public async Task DowngradedOverLimitWorkspaceCannotReplaceAnArchivedProjectWithANewActiveProject()
    {
        var repository = new InMemoryWorkspaceRepository();
        repository.Seed("user", new WorkspaceSnapshot
        {
            Projects =
            [
                new() { Id = "one", Name = "One" },
                new() { Id = "two", Name = "Two" },
                new() { Id = "three", Name = "Three" },
                new() { Id = "four", Name = "Four" }
            ]
        }, "revision-1");
        var request = repository.Read("user");
        request.Projects.Single(project => project.Id == "four").IsArchived = true;
        request.Projects.Add(new() { Id = "new", Name = "New project" });

        var result = await CreateService(repository).SaveAsync("user", new(request, "revision-1"));

        Assert.That(result.ErrorCode, Is.EqualTo(WorkspaceSaveErrorCode.ProjectLimit));
        Assert.That(repository.SaveCount, Is.Zero);
    }

    private static WorkspaceService CreateService(IWorkspaceRepository repository) =>
        new(repository, new EmptyActivityArchiveRepository(), new StubBillingRepository());

    private sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
    {
        private readonly Dictionary<string, WorkspaceDocument> _documents = new(StringComparer.Ordinal);
        public int SaveCount { get; private set; }

        public void Seed(string ownerId, WorkspaceSnapshot snapshot, string revision) =>
            _documents[ownerId] = new WorkspaceDocument
            {
                UserId = ownerId,
                Payload = JsonSerializer.Serialize(snapshot, SharedCommon.JsonOptions),
                ETag = revision
            };

        public WorkspaceSnapshot Read(string ownerId) =>
            JsonSerializer.Deserialize<WorkspaceSnapshot>(_documents[ownerId].Payload, SharedCommon.JsonOptions)!;

        public Task<WorkspaceDocument?> GetAsync(string ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_documents.TryGetValue(ownerId, out var document) ? document : null);

        public Task<WorkspaceDocument?> SaveAsync(string ownerId, WorkspaceSnapshot workspace, string? expectedRevision, CancellationToken cancellationToken = default)
        {
            if (_documents.TryGetValue(ownerId, out var existing) && !string.Equals(existing.ETag, expectedRevision, StringComparison.Ordinal))
            {
                return Task.FromResult<WorkspaceDocument?>(null);
            }

            SaveCount++;
            var saved = new WorkspaceDocument
            {
                UserId = ownerId,
                Payload = JsonSerializer.Serialize(workspace, SharedCommon.JsonOptions),
                ETag = $"revision-{SaveCount + 1}"
            };
            _documents[ownerId] = saved;

            return Task.FromResult<WorkspaceDocument?>(saved);
        }
    }

    private sealed class EmptyActivityArchiveRepository : IActivityArchiveRepository
    {
        public Task ArchiveAsync(string ownerId, IReadOnlyCollection<FocusSessionRecord> sessions, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
