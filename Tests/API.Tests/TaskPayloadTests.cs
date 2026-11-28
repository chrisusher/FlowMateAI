using System.Text.Json;
using ChrisUsher.Core.Shared;
using Shared.Contracts;
using Shared.Models;

namespace API.Tests;

public sealed class TaskPayloadTests
{
    [Test]
    public void TaskWriteRequest_DefaultValuedFields_RoundTripsIntoRequiredServerFields()
    {
        var sent = new TaskWriteRequest(new TaskRecord
        {
            Id = "task",
            Title = "Task",
            ProjectId = "project"
        }, "revision", "workspace-revision");
        var json = JsonSerializer.Serialize(sent, SharedCommon.JsonOptions);
        var received = JsonSerializer.Deserialize<TaskCreateRequest>(json, SharedCommon.JsonOptions);

        Assert.That(received!.Task!.Id, Is.EqualTo("task"));
        Assert.That(received.Task.IsComplete, Is.False);
        Assert.That(received.Task.PlannedToday, Is.False);
        Assert.That(received.Task.FocusMinutesToday, Is.Zero);
        Assert.That(received.Task.Priority, Is.Not.Null);
        Assert.That(received.Task.CreatedAt, Is.Not.Null);
        Assert.That(received.Revision, Is.EqualTo("revision"));
        Assert.That(received.WorkspaceRevision, Is.EqualTo("workspace-revision"));
    }
}
