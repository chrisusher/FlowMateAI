using System.Net;
using System.Net.Http.Json;
using Shared.Contracts;
using Web.Components.Features.Reports;

namespace Web.Tests;

public sealed class TaskFocusComponentTests : WorkspaceComponentTest
{
    [Test]
    public void TaskFocus_RequestFailure_ShowsError()
    {
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = Render<TaskFocusCard>(parameters => parameters.Add(card => card.Date, new DateOnly(2026, 11, 27)));
        cut.WaitForAssertion(() => Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("could not be loaded")));
    }

    [Test]
    public async Task TaskFocus_DateChanges_IgnoresOlderResponse()
    {
        var first = new TaskCompletionSource<HttpResponseMessage>();
        Api.Respond = (request, _) => request.RequestUri!.Query.Contains("2026-11-26")
            ? first.Task
            : Task.FromResult(Json([new TaskFocus("new", "New task", 12, 0.5)]));
        var cut = Render<TaskFocusCard>(parameters => parameters.Add(card => card.Date, new DateOnly(2026, 11, 26)));
        Assert.That(cut.Markup, Does.Contain("Loading"));
        cut.Render(parameters => parameters.Add(card => card.Date, new DateOnly(2026, 11, 27)));
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("New task")));
        await cut.InvokeAsync(async () =>
        {
            first.SetResult(Json([new TaskFocus("old", "Old task", 4, 1)]));
            await Task.Yield();
        });
        Assert.That(cut.Markup, Does.Contain("New task"));
        Assert.That(cut.Markup, Does.Not.Contain("Old task"));
    }

    private static HttpResponseMessage Json(TaskFocus[] tasks) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(tasks)
    };
}
