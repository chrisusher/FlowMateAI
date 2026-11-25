using Services.Repositories;
using Services.Workspaces;
using Shared.Enums;

namespace Services.Tests.Workspaces;

public sealed class TaskServiceTests
{
    [Test]
    public async Task CreateAsync_InvalidTaskOrMissingRevision_RejectsBeforePersistence()
    {
        var repository = new RecordingRepository();
        var service = new TaskService(repository);
        var task = new TaskRecord
        {
            Id = "task",
            Title = " ",
            ProjectId = "project"
        };

        Assert.That((await service.CreateAsync("user", task, "revision")).Rejection, Is.EqualTo(TaskChangeRejection.InvalidInput));
        task.Title = "Valid";
        Assert.That((await service.CreateAsync("user", task, "")).Rejection, Is.EqualTo(TaskChangeRejection.InvalidInput));
        task.Id = "bad/id";
        Assert.That((await service.CreateAsync("user", task, "revision")).Rejection, Is.EqualTo(TaskChangeRejection.InvalidInput));
        Assert.That(repository.Calls, Is.Zero);
    }

    [Test]
    public async Task UpdateAsync_ValidTask_TrimsTitleAndForwardsOwnerAndRevisions()
    {
        var repository = new RecordingRepository();
        var service = new TaskService(repository);
        var result = await service.UpdateAsync("user", new TaskRecord
        {
            Id = "task",
            Title = "  Good task  ",
            ProjectId = "project"
        }, "task-revision", "workspace-revision");

        Assert.That(result.Rejection, Is.EqualTo(TaskChangeRejection.None));
        Assert.That(repository.Owner, Is.EqualTo("user"));
        Assert.That(repository.Title, Is.EqualTo("Good task"));
        Assert.That(repository.TaskRevision, Is.EqualTo("task-revision"));
        Assert.That(repository.WorkspaceRevision, Is.EqualTo("workspace-revision"));
    }

    private sealed class RecordingRepository : ITaskRepository
    {
        public int Calls { get; private set; }
        public string? Owner { get; private set; }
        public string? Title { get; private set; }
        public string? TaskRevision { get; private set; }
        public string? WorkspaceRevision { get; private set; }

        public Task<TaskWriteResult?> RecoverAsync(string userId, CancellationToken cancellationToken = default) => Task.FromResult<TaskWriteResult?>(null);
        public Task<TaskWriteResult> GetAsync(string userId, string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TaskWriteResult(null, null, TaskChangeRejection.NotFound));
        public Task<TaskWriteResult> CreateAsync(string userId, TaskRecord task, string workspaceRevision, CancellationToken cancellationToken = default) =>
            UpdateAsync(userId, task, "", workspaceRevision, cancellationToken);
        public Task<TaskWriteResult> DeleteAsync(string userId, string id, string revision, string workspaceRevision, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TaskWriteResult(null, null, TaskChangeRejection.NotFound));

        public Task<TaskWriteResult> UpdateAsync(string userId, TaskRecord task, string revision, string workspaceRevision, CancellationToken cancellationToken = default)
        {
            Calls++;
            Owner = userId;
            Title = task.Title;
            TaskRevision = revision;
            WorkspaceRevision = workspaceRevision;

            return Task.FromResult(new TaskWriteResult(task, "revision", TaskChangeRejection.None));
        }
    }
}
