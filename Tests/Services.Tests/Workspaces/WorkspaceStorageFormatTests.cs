using System.Text.Json;
using ChrisUsher.Core.Shared;
using Services.Repositories;
using Services.Database;

namespace Services.Tests.Workspaces;

public sealed class WorkspaceStorageFormatTests
{
    [Test]
    public void LegacyWorkspaceCanBeBackfilledAndWrittenAsStructuredData()
    {
        var original = new WorkspaceSnapshot
        {
            DisplayName = "Test user",
            Sessions =
            [
                new() { Id = "one", FocusSeconds = 600, ProjectId = "project", TaskId = "task" },
                new() { Id = "two", FocusMinutes = 15, ProjectId = "project", PlannedSeconds = 1800 }
            ]
        };
        var legacy = new WorkspaceDocument { Payload = JsonSerializer.Serialize(original, SharedCommon.JsonOptions) };
        var workspace = WorkspaceStorageFormat.Read(legacy);

        Assert.That(WorkspaceStorageFormat.BackfillPlannedSeconds(workspace, 1500), Is.EqualTo(1));
        Assert.That(workspace.Sessions.Select(s => s.PlannedSeconds), Is.EqualTo(new int?[] { 1500, 1800 }));
        Assert.That(FocusSessionRecord.TotalSessions(workspace.Sessions), Is.EqualTo(0.9).Within(0.00001));
        var document = new WorkspaceDocument { Workspace = workspace };
        Assert.That(document.Payload, Is.Null);
        var restored = WorkspaceStorageFormat.Read(document);
        Assert.That(restored.DisplayName, Is.EqualTo("Test user"));
        Assert.That(restored.Sessions[0].TaskId, Is.EqualTo("task"));
        Assert.That(restored.Sessions[0].PlannedSeconds, Is.EqualTo(1500));
        Assert.That(WorkspaceStorageFormat.BackfillPlannedSeconds(restored, 1500), Is.Zero);
    }

    [Test]
    public void DerivedRecordsContainQueryableObjectsInsteadOfJsonStrings()
    {
        var workspace = new WorkspaceSnapshot { Sessions = [new() { Id = "session", FocusSeconds = 600, PlannedSeconds = 1500 }] };
        var record = WorkspaceRecordDocument.FromSnapshot("user", workspace).Single(x => x.Id == "focus:session");
        Assert.That(record.Payload, Is.Null);
        Assert.That(record.Focus, Is.Not.Null);
        Assert.That(record.Focus!.PlannedSeconds, Is.EqualTo(1500));
    }

    [Test]
    public void MissingOrMalformedWorkspaceIsNeverSilentlyReplacedWithAnEmptyOne()
    {
        Assert.That(() => WorkspaceStorageFormat.Read(new WorkspaceDocument()), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => WorkspaceStorageFormat.Read(new WorkspaceDocument { Payload = "not json" }), Throws.TypeOf<JsonException>());
    }
}
