using System.Net;
using System.Net.Http.Json;
using Shared.Contracts;
using Web.Components.Features.Settings;

namespace Web.Tests;

public sealed class IntegrationsComponentTests : WorkspaceComponentTest
{
    [Test]
    public async Task FailedKeyListCanBeRetriedWithoutShowingAnEmptyList()
    {
        await SignInAsync();
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var cut = Render<IntegrationsCard>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Could not load assistant keys")));
        Assert.That(cut.Markup, Does.Not.Contain("No assistant keys yet"));
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<McpKeySummary>()) });
        cut.FindAll("button").Single(b => b.TextContent.Contains("Retry loading")).Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("No assistant keys yet")));
        Assert.That(cut.Markup, Does.Not.Contain("Could not load assistant keys"));
    }

    [Test]
    public async Task CreatedSecretIsShownOnceAndCanBeDismissed()
    {
        await SignInAsync();
        Configuration["McpEndpoint"] = "https://mcp.example.test/runtime/webhooks/mcp";
        Api.Respond = (request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new McpKeyCreatedResponse("id", "Laptop", "fm", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90), "secret-value")) }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<McpKeySummary>()) });
        var cut = Render<IntegrationsCard>();
        cut.Find("input").Change("Laptop");
        cut.FindAll("button").Single(b => b.TextContent.Contains("Create key")).Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("secret-value")));
        cut.Find("input").Change("Another assistant");
        Assert.That(cut.FindAll("button").Single(b => b.TextContent.Contains("Create key")).HasAttribute("disabled"), Is.True);
        cut.FindAll("button").Single(b => b.TextContent == "Done").Click();
        Assert.That(cut.Markup, Does.Not.Contain("secret-value"));
        Assert.That(cut.FindAll("button").Single(b => b.TextContent.Contains("Create key")).HasAttribute("disabled"), Is.False);
    }
    [Test]
    public async Task RevocationRefreshesSafeMetadataAndDisplaysRevokedState()
    {
        await SignInAsync();
        var revoked = false;
        var now = DateTimeOffset.UtcNow;
        Api.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                revoked = true;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            var keys = new[] { new McpKeySummary("key-1", "Laptop", "fm_abc", now, now.AddDays(30), revoked) };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(keys) });
        };
        var cut = Render<IntegrationsCard>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Active")));
        cut.FindAll("button").Single(b => b.TextContent.Contains("Revoke")).Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Revoked")));
        Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("Revoke")), Is.False);
        Assert.That(Api.Paths, Does.Contain("/api/v1/mcp-keys/key-1"));
    }
}
