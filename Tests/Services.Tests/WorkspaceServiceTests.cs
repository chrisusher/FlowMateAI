using System.Text.Json;
using ChrisUsher.Core.Shared;
using NUnit.Framework;
using Services.Database;
using Services.Repositories;
using Services.Workspaces;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;

namespace Services.Tests;

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

    private static WorkspaceService CreateService(IWorkspaceRepository repository) =>
        new(repository, new EmptyActivityArchiveRepository(), new FreeBillingRepository());

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

    private sealed class FreeBillingRepository : IBillingRepository
    {
        public Task<BillingEntitlementDocument> GetEntitlementAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BillingEntitlementDocument { UserId = userId, Plan = BillingPlan.Free.ToString() });

        public Task SaveEntitlementAsync(BillingEntitlementDocument entitlement, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> HasProcessedEventAsync(string eventId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task MarkEventProcessedAsync(string eventId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
