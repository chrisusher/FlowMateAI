using System.Net;
using ChrisUsher.Core.Shared;
using Shared.Contracts;
using Shared.Enums;
using Web.Clients;

namespace Web.Tests;

public sealed class WorkspaceItemsClientTests : WorkspaceComponentTest
{
    [Test]
    public async Task ListsUseAuthenticatedCollectionRoutes()
    {
        await SignInAsync();
        Api.Respond = (request, _) =>
        {
            Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer test-token"));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<object>(), options: SharedCommon.JsonOptions)
            });
        };
        var client = new WorkspaceItemsClient(Services.GetRequiredService<HttpClient>(), Auth);

        await client.ListProjectsAsync();
        await client.ListTasksAsync();
        await client.ListSessionsAsync();
        await client.ListConversationsAsync();

        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/projects", "/api/v1/tasks", "/api/v1/sessions", "/api/v1/conversations" }));
    }

    [Test]
    public async Task MissingItemReturnsNullAndOtherFailuresAreReported()
    {
        await SignInAsync();
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = new WorkspaceItemsClient(Services.GetRequiredService<HttpClient>(), Auth);

        Assert.That(await client.GetProjectAsync("missing"), Is.Null);
        Api.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        _ = Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetTaskAsync("missing"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WorkspaceLoadUsesMatchingCollectionsAndFallsBackWhenTheyAreStale(bool stale)
    {
        await SignInAsync();
        var snapshot = new WorkspaceSnapshot
        {
            FirstRunState = OnboardingState.Dismissed,
            Projects =
            [
                new ProjectRecord
                {
                    Id = "project",
                    Name = "Current"
                }
            ],
            Tasks =
            [
                new TaskRecord
                {
                    Id = "task",
                    Title = "Current"
                }
            ]
        };
        var remote = new WorkspaceResponse(snapshot, "revision");
        var fallbackRequested = false;
        Api.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(remote, options: SharedCommon.JsonOptions)
                });
            }

            object body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/workspace" when request.RequestUri.Query.Length != 0 => WorkspaceCollectionManifest.Create(remote),
                "/api/v1/workspace" => Fallback(),
                "/api/v1/projects" => stale ? Array.Empty<ProjectRecord>() : snapshot.Projects,
                "/api/v1/tasks" => snapshot.Tasks,
                "/api/v1/sessions" => snapshot.Sessions,
                "/api/v1/conversations" => snapshot.Conversations,
                _ => throw new InvalidOperationException("Unexpected request")
            };

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body, body.GetType(), options: SharedCommon.JsonOptions)
            };

            if (request.RequestUri.AbsolutePath == "/api/v1/workspace" && request.RequestUri.Query.Length != 0)
            {
                response.Headers.Add("X-Workspace-Collections", "separate");
            }

            return Task.FromResult(response);
        };

        WorkspaceResponse Fallback()
        {
            fallbackRequested = true;

            return remote;
        }

        await Store.InitialiseAsync();

        Assert.That(Store.Data.Projects.Single().Name, Is.EqualTo("Current"));
        Assert.That(Store.Data.Tasks.Single().Title, Is.EqualTo("Current"));
        Assert.That(fallbackRequested, Is.EqualTo(stale));
        Assert.That(Api.Paths, Does.Contain("/api/v1/conversations"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task IncompleteManifestOrUnavailableCollectionFallsBackToTheFullWorkspace(bool incompleteManifest)
    {
        await SignInAsync();
        var snapshot = new WorkspaceSnapshot
        {
            FirstRunState = OnboardingState.Dismissed,
            Projects =
            [
                new ProjectRecord
                {
                    Id = "project",
                    Name = "Preserved"
                }
            ]
        };
        var remote = new WorkspaceResponse(snapshot, "revision");
        var fallbackRequested = false;
        Api.Respond = (request, _) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(remote, options: SharedCommon.JsonOptions)
                });
            }

            if (request.RequestUri!.AbsolutePath != "/api/v1/workspace")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var isManifest = request.RequestUri.Query.Length != 0;

            object body = isManifest
                ? incompleteManifest ? new
                {
                    workspace = new WorkspaceSnapshot(),
                    revision = "revision"
                } : WorkspaceCollectionManifest.Create(remote)
                : remote;

            fallbackRequested |= !isManifest;

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body, body.GetType(), options: SharedCommon.JsonOptions)
            };

            if (isManifest)
            {
                response.Headers.Add("X-Workspace-Collections", "separate");
            }

            return Task.FromResult(response);
        };

        await Store.InitialiseAsync();

        Assert.That(fallbackRequested, Is.True);
        Assert.That(Store.Data.Projects.Single().Name, Is.EqualTo("Preserved"));
    }
}
