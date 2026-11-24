using System.Net;
using ChrisUsher.Core.Shared;
using Shared.Contracts;
using Shared.Enums;
using Web.Clients;

namespace Web.Tests;

public sealed class WorkspaceItemsClientTests : WorkspaceComponentTest
{
    [Test]
    public async Task ProjectWritesUseAuthenticatedResourceRoutesAndRecordRevisions()
    {
        await SignInAsync();
        var project = new ProjectRecord
        {
            Id = "project-one",
            Name = "Project"
        };
        Api.Respond = async (request, cancellationToken) =>
        {
            Assert.That(request.Headers.Authorization?.ToString(), Is.EqualTo("Bearer test-token"));

            if (request.Method == HttpMethod.Get)
            {
                var read = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(project, options: SharedCommon.JsonOptions)
                };
                read.Headers.TryAddWithoutValidation("ETag", "\"revision-1\"");

                return read;
            }

            if (request.Method == HttpMethod.Post)
            {
                var create = await request.Content!.ReadFromJsonAsync<ProjectCreateRequest>(SharedCommon.JsonOptions, cancellationToken);
                Assert.That(create!.Id, Is.EqualTo(project.Id));
            }
            else
            {
                var rename = await request.Content!.ReadFromJsonAsync<ProjectRenameRequest>(SharedCommon.JsonOptions, cancellationToken);
                Assert.That(rename!.Revision, Is.EqualTo("\"revision-1\""));
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ProjectWriteResponse(project, "\"revision-2\""), options: SharedCommon.JsonOptions)
            };
        };
        var client = new WorkspaceItemsClient(Services.GetRequiredService<HttpClient>(), Auth);

        var current = await client.GetProjectVersionedAsync(project.Id);
        await client.CreateProjectAsync(new ProjectCreateRequest(project.Id, project.Name, project.Color, project.CreatedAt));
        var renamed = await client.RenameProjectAsync(project.Id, new ProjectRenameRequest("New name", current!.Revision));

        Assert.That(renamed.Revision, Is.EqualTo("\"revision-2\""));
        Assert.That(Api.Paths, Is.EqualTo(new[] { "/api/v1/projects/project-one", "/api/v1/projects", "/api/v1/projects/project-one" }));
    }

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

    [Test]
    public async Task WorkspaceCollectionRequestsCanRunConcurrently()
    {
        await SignInAsync();
        var remote = new WorkspaceResponse(new WorkspaceSnapshot
        {
            FirstRunState = OnboardingState.Dismissed
        }, "revision");

        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        Api.Respond = async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(remote, options: SharedCommon.JsonOptions)
                };
            }

            if (request.RequestUri!.AbsolutePath == "/api/v1/workspace/metadata")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(WorkspaceMetadataResponse.Create(remote), options: SharedCommon.JsonOptions)
                };
                response.Headers.Add("X-Workspace-Collections", "separate");

                return response;
            }

            if (Interlocked.Increment(ref started) == 5)
            {
                allStarted.TrySetResult();
            }

            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath.EndsWith("/timer", StringComparison.Ordinal)
                    ? JsonContent.Create(remote.Workspace.Timer, options: SharedCommon.JsonOptions)
                    : JsonContent.Create(Array.Empty<object>(), options: SharedCommon.JsonOptions)
            };
        };

        await Store.InitialiseAsync();

        Assert.That(started, Is.EqualTo(5));
        Assert.That(Store.RequiresRemoteReload, Is.False);
    }

    [Test]
    public async Task NavigationRefreshUpdatesCoachMessagesForTheNewActiveConversation()
    {
        await SignInAsync();
        var snapshot = new WorkspaceSnapshot
        {
            FirstRunState = OnboardingState.Dismissed,
            Conversations =
            [
                new ConversationRecord
                {
                    Id = "old",
                    Messages =
                    [
                        new ChatMessageRecord
                        {
                            Role = ChatMessageRole.Assistant,
                            Text = "Old message"
                        }
                    ]
                }
            ]
        };
        Api.Respond = async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var saved = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions, cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new WorkspaceResponse(saved!.Workspace, "revision"), options: SharedCommon.JsonOptions)
                };
            }

            object body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/workspace/metadata" => WorkspaceMetadataResponse.Create(new WorkspaceResponse(snapshot, "revision")),
                "/api/v1/workspace/timer" => snapshot.Timer,
                "/api/v1/projects" => snapshot.Projects,
                "/api/v1/tasks" => snapshot.Tasks,
                "/api/v1/sessions" => snapshot.Sessions,
                "/api/v1/conversations" => snapshot.Conversations,
                _ => throw new InvalidOperationException("Unexpected request")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body, body.GetType(), options: SharedCommon.JsonOptions)
            };
        };

        await Store.InitialiseAsync();
        Assert.That(Coach.ChatMessages.Single().Content, Is.EqualTo("Old message"));
        snapshot.Conversations =
        [
            new ConversationRecord
            {
                Id = "new",
                Messages =
                [
                    new ChatMessageRecord
                    {
                        Role = ChatMessageRole.Assistant,
                        Text = "New message"
                    }
                ]
            }
        ];

        await Store.RefreshForNavigationAsync();

        Assert.That(Coach.ActiveConversation.Id, Is.EqualTo("new"));
        Assert.That(Coach.ChatMessages.Single().Content, Is.EqualTo("New message"));
    }

    [Test]
    public async Task NavigationRefreshDoesNotOverwriteAnEditMadeWhileReadsAreInFlight()
    {
        await SignInAsync();
        var snapshot = new WorkspaceSnapshot
        {
            DisplayName = "Server",
            FirstRunState = OnboardingState.Dismissed
        };
        var remote = new WorkspaceResponse(snapshot, "revision");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = false;
        
        Api.Respond = async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(remote, options: SharedCommon.JsonOptions)
                };
            }

            var path = request.RequestUri!.AbsolutePath;

            if (delay && path == "/api/v1/projects")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            object body = path switch
            {
                "/api/v1/workspace/metadata" => WorkspaceMetadataResponse.Create(remote),
                "/api/v1/workspace/timer" => snapshot.Timer,
                "/api/v1/projects" => snapshot.Projects,
                "/api/v1/tasks" => snapshot.Tasks,
                "/api/v1/sessions" => snapshot.Sessions,
                "/api/v1/conversations" => snapshot.Conversations,
                _ => throw new InvalidOperationException("Unexpected request")
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body, body.GetType(), options: SharedCommon.JsonOptions)
            };
        };
        await Store.InitialiseAsync();
        delay = true;
        var refresh = Store.RefreshForNavigationAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Store.Data.DisplayName = "Local edit";
        release.TrySetResult();
        await refresh;

        Assert.That(Store.Data.DisplayName, Is.EqualTo("Local edit"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SlowCollectionReadsDoNotMakeTheServerClockLag(bool retry)
    {
        await SignInAsync();

        if (retry)
        {
            await Store.InitialiseAsync();
            Assert.That(Store.RequiresRemoteReload, Is.True);
        }

        var serverTime = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var snapshot = new WorkspaceSnapshot
        {
            FirstRunState = OnboardingState.Dismissed,
            Timer = new TimerSnapshot
            {
                Phase = TimerPhase.Focus,
                OwnerClientId = "test-device",
                EndsAt = serverTime.AddMinutes(5)
            }
        };
        var remote = new WorkspaceResponse(snapshot, "revision");
        var elapsed = new System.Diagnostics.Stopwatch();
        Api.Respond = async (request, cancellationToken) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                var saved = await request.Content!.ReadFromJsonAsync<WorkspaceSaveRequest>(SharedCommon.JsonOptions, cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new WorkspaceResponse(saved!.Workspace, "revision"), options: SharedCommon.JsonOptions)
                };
            }

            if (request.RequestUri!.AbsolutePath == "/api/v1/workspace/metadata")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(WorkspaceMetadataResponse.Create(remote), options: SharedCommon.JsonOptions)
                };
                response.Headers.Add("X-Workspace-Collections", "separate");
                response.Headers.Date = serverTime;
                elapsed.Start();

                return response;
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/projects")
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri.AbsolutePath.EndsWith("/timer", StringComparison.Ordinal)
                    ? JsonContent.Create(snapshot.Timer, options: SharedCommon.JsonOptions)
                    : JsonContent.Create(Array.Empty<object>(), options: SharedCommon.JsonOptions)
            };
        };

        if (retry)
        {
            await Store.RetrySaveAsync();
        }
        else
        {
            await Store.InitialiseAsync();
        }

        // Allow for timer resolution while still verifying a multi-second collection delay.
        Assert.That(elapsed.Elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(2.9)));
        Assert.That((Store.UtcNow - serverTime - elapsed.Elapsed).Duration(), Is.LessThan(TimeSpan.FromSeconds(1)));
        Assert.That(Store.Data.Timer.RemainingSeconds, Is.EqualTo(Math.Ceiling(300 - elapsed.Elapsed.TotalSeconds)).Within(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WorkspaceLoadPreservesLocalDataWhenACollectionDoesNotMatch(bool stale)
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
                "/api/v1/workspace/metadata" => WorkspaceMetadataResponse.Create(remote),
                "/api/v1/workspace/timer" => snapshot.Timer,
                "/api/v1/projects" => stale ? Array.Empty<ProjectRecord>() : snapshot.Projects,
                "/api/v1/tasks" => snapshot.Tasks,
                "/api/v1/sessions" => snapshot.Sessions,
                "/api/v1/conversations" => snapshot.Conversations,
                _ => throw new InvalidOperationException("Unexpected request")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body, body.GetType(), options: SharedCommon.JsonOptions)
            });
        };

        await Store.InitialiseAsync();

        Assert.That(Store.RequiresRemoteReload, Is.EqualTo(stale));
        Assert.That(Store.Data.Projects.Single().Name, Is.EqualTo(stale ? "Personal" : "Current"));
        Assert.That(Api.Paths, Does.Contain("/api/v1/conversations"));
        Assert.That(Api.Paths.Count(path => path == "/api/v1/workspace"), Is.EqualTo(stale ? 0 : 1)); // PUT only
    }
}
