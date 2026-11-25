using System.Net;
using System.Net.Http.Json;
using Shared.Contracts;
using ChrisUsher.Core.Shared;
using Web.Components.Features.Reports;

namespace Web.Tests;

public sealed class FocusTimelineComponentTests : WorkspaceComponentTest
{
    [Test]
    public async Task Timeline_LoadingThenEmpty_ShowsStatusAndNavigation()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        Api.Respond = (_, _) => pending.Task;
        var cut = Render<FocusTimelineCard>();
        Assert.That(cut.Find("[role=status]").TextContent, Does.Contain("Loading"));
        var start = new DateOnly(2026, 11, 23);
        await cut.InvokeAsync(async () =>
        {
            pending.SetResult(Json(Timeline(start, [])));
            await Task.Yield();
        });
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("No recorded focus")));
        Assert.That(cut.Find("button[aria-label='Next week']").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find("button[aria-label='Previous week']").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void Timeline_EntryAndPartialHistory_ExposesAccessibleDetails()
    {
        var start = new DateOnly(2026, 11, 23);
        var entry = new FocusTimelineEntry("s", "Project", "#123456", "Task", start,
            new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 23, 10, 30, 0, TimeSpan.Zero), 900, true);
        Api.Respond = (_, _) => Task.FromResult(Json(Timeline(start, [entry], true)));
        var cut = Render<FocusTimelineCard>();
        cut.WaitForAssertion(() => Assert.That(cut.Find(".timeline-block").GetAttribute("aria-label"), Does.Contain("Project, Task, 10:00 +00:00 to 10:30 +00:00, 15 minutes focused, approximate")));
        Assert.That(cut.Markup, Does.Contain("partially available"));
        Assert.That(cut.Find(".timeline-block").GetAttribute("tabindex"), Is.EqualTo("0"));
        Assert.That(cut.Find(".timeline-block").GetAttribute("style"), Does.Contain("background-color:#123456"));
    }

    [Test]
    public void Timeline_RequestFailure_ShowsErrorAndRetry()
    {
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var cut = Render<FocusTimelineCard>();
        cut.WaitForAssertion(() => Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("could not be loaded")));
        Assert.That(cut.Find("[role=alert] button").TextContent, Does.Contain("Try again"));
    }

    [Test]
    public void Timeline_PreviousWeek_RequestsIndependentWeek()
    {
        var start = new DateOnly(2026, 11, 23);
        Api.Respond = (request, _) => Task.FromResult(Json(Timeline(request.RequestUri!.Query.Contains("2026-11-16") ? start.AddDays(-7) : start, [])));
        var cut = Render<FocusTimelineCard>();
        cut.WaitForAssertion(() => Assert.That(cut.Find("button[aria-label='Previous week']").HasAttribute("disabled"), Is.False));
        cut.Find("button[aria-label='Previous week']").Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("16 Nov 2026")));
    }

    [Test]
    public async Task Timeline_RecordedFocusSaved_RefreshesFromServer()
    {
        await SignInAsync();
        var start = new DateOnly(2026, 11, 23);
        var calls = 0;
        Api.Respond = (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("focus-timeline", StringComparison.Ordinal))
            {
                calls++;

                return Task.FromResult(Json(Timeline(start, [])));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new WorkspaceResponse(Store.Data, "revision"), options: SharedCommon.JsonOptions)
            });
        };
        var cut = Render<FocusTimelineCard>();
        cut.WaitForAssertion(() => Assert.That(calls, Is.EqualTo(1)));
        AddSession(5);

        await cut.InvokeAsync(() => Store.SaveAsync());

        cut.WaitForAssertion(() => Assert.That(calls, Is.EqualTo(2)));
    }

    private static FocusTimeline Timeline(DateOnly start, IReadOnlyList<FocusTimelineEntry> entries, bool partial = false) =>
        new(start, start.AddDays(6), "Europe/London", partial ? start.AddDays(1) : start.AddDays(-29), start.AddDays(2), partial, entries);

    private static HttpResponseMessage Json(FocusTimeline timeline) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(timeline)
    };
}
