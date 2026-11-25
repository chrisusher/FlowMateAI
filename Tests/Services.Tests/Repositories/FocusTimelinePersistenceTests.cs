using Microsoft.EntityFrameworkCore;
using Services.Database;
using Services.Reporting;
using Services.Repositories;

namespace Services.Tests.Repositories;

public sealed class FocusTimelinePersistenceTests
{
    [Test]
    public async Task GetAsync_UserScopedWorkspace_ReturnsOnlyRequestedUsersSessions()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        await using var database = testDatabase.CreateContext();
        var instant = DateTimeOffset.UtcNow.AddMinutes(-5);

        foreach (var user in new[] { "first", "second" })
        {
            database.Workspaces.Add(new WorkspaceDocument
            {
                UserId = user,
                Workspace = new WorkspaceSnapshot
                {
                    TimeZone = "UTC",
                    Sessions = [new FocusSessionRecord
                    {
                        Id = user,
                        ProjectId = "project",
                        StartedAt = instant,
                        EndedAt = instant.AddMinutes(1),
                        FocusSeconds = 60,
                        Intervals = [new FocusIntervalRecord
                        {
                            StartedAt = instant,
                            EndedAt = instant.AddMinutes(1)
                        }]
                    }]
                }
            });
            await database.SaveChangesAsync();
            database.ChangeTracker.Clear();
        }

        database.ChangeTracker.Clear();
        var service = new FocusTimelineService(new WorkspaceReportRepository(database));

        var result = await service.GetAsync("first", null);

        Assert.That(result.Entries.Select(entry => entry.SessionId), Is.EqualTo(new[] { "first" }));
        Assert.That(result.Entries.Single().IsApproximate, Is.False);
    }

    [Test]
    public async Task SaveChangesAsync_FocusIntervals_RoundTripsThroughCosmos()
    {
        await using var testDatabase = await CosmosTestEnvironment.CreateDatabaseAsync();
        var start = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
        await using (var writer = testDatabase.CreateContext())
        {
            writer.Sessions.Add(new WorkspaceSessionDocument
            {
                UserId = "user",
                Id = "session",
                Session = new FocusSessionRecord
                {
                    Id = "session",
                    ProjectId = "project",
                    StartedAt = start,
                    EndedAt = start.AddMinutes(10),
                    FocusSeconds = 300,
                    Intervals = [new FocusIntervalRecord
                    {
                        StartedAt = start,
                        EndedAt = start.AddMinutes(5)
                    }]
                }
            });
            await writer.SaveChangesAsync();
        }

        await using var reader = testDatabase.CreateContext();
        var session = (await reader.Sessions.AsNoTracking().SingleAsync(document => document.UserId == "user")).Session!;
        Assert.That(session.Intervals.Single().StartedAt, Is.EqualTo(start));
        Assert.That(session.Intervals.Single().EndedAt, Is.EqualTo(start.AddMinutes(5)));
    }
}
