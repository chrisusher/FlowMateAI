using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChrisUsher.Core.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Radzen;
using Shared.Contracts;
using Shared.Enums;
using Web.Clients;
using Web.Managers;

namespace Web.Tests;

public abstract class WorkspaceComponentTest : BunitContext
{
    protected TestApiHandler Api { get; } = new();
    protected BunitJSModuleInterop WorkspaceModule { get; }
    protected BunitJSModuleInterop AuthModule { get; }
    protected IConfigurationRoot Configuration { get; }
    protected WorkspaceStore Store => Services.GetRequiredService<WorkspaceStore>();
    protected WorkspaceStatistics Stats => Services.GetRequiredService<WorkspaceStatistics>();
    protected WorkspaceTaskManager Tasks => Services.GetRequiredService<WorkspaceTaskManager>();
    protected WorkspaceProjectManager Projects => Services.GetRequiredService<WorkspaceProjectManager>();
    protected WorkspaceTimerManager Timer => Services.GetRequiredService<WorkspaceTimerManager>();
    protected WorkspaceCoachManager Coach => Services.GetRequiredService<WorkspaceCoachManager>();
    protected WorkspaceBillingManager Billing => Services.GetRequiredService<WorkspaceBillingManager>();
    protected Auth0Client Auth => Services.GetRequiredService<Auth0Client>();
    protected NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();
    protected ProjectRecord Project => Store.Data.Projects[0];
    protected TaskRecord PlannedTask => Store.Data.Tasks[0];

    protected WorkspaceComponentTest()
    {
        // Radzen's DOM measurements are irrelevant to component behavior; application
        // storage and authentication calls have explicit results and remain observable.
        JSInterop.Mode = JSRuntimeMode.Loose;
        WorkspaceModule = JSInterop.SetupModule("./js/workspace.js");
        WorkspaceModule.Setup<string>("getClientId").SetResult("test-device");
        WorkspaceModule.Setup<string?>("read", _ => true).SetResult(null);
        AuthModule = JSInterop.SetupModule("./js/auth0.js");
        AuthModule.Setup<AuthSession>("initialize", _ => true).SetResult(new());
        Configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        Services.AddSingleton<IConfiguration>(Configuration);
        Services.AddSingleton(new HttpClient(Api) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<Auth0Client>();
        Services.AddScoped<BillingClient>();
        Services.AddScoped<WorkspaceStore>();
        Services.AddScoped<WorkspaceStatistics>();
        Services.AddScoped<WorkspaceTaskManager>();
        Services.AddScoped<WorkspaceProjectManager>();
        Services.AddScoped<WorkspaceTimerManager>();
        Services.AddScoped<WorkspaceBillingManager>();
        Services.AddScoped<WorkspaceCoachManager>();
        Services.AddScoped<WorkspaceAppearanceManager>();
        Services.AddRadzenComponents();
        Store.Data.DisplayName = "Alex Morgan";
        Store.Data.Projects.Add(new() { Id = "project-one", Name = "Personal", Color = "#5b68e8" });
        Store.Data.Tasks.Add(new() { Id = "task-one", Title = "Choose one thing", ProjectId = "project-one", PlannedToday = true, Priority = TaskPriority.High });
        Store.Data.Tasks.Add(new() { Id = "task-two", Title = "Take a reset", ProjectId = "project-one", Priority = TaskPriority.Low });
        Coach.Initialize();
    }

    protected void PreserveWorkspaceForInitialization() => WorkspaceModule
        .Setup<string?>("read", _ => true)
        .SetResult(JsonSerializer.Serialize(Store.Data, SharedCommon.JsonOptions));

    protected async Task SignInAsync()
    {
        Configuration["Auth0:Domain"] = "auth.example.test";
        Configuration["Auth0:ClientId"] = "test-client";
        Configuration["Auth0:Audience"] = "test-api";
        AuthModule.Setup<AuthSession>("initialize", _ => true).SetResult(new()
        {
            Configured = true,
            SignedIn = true,
            Sub = "test-user",
            Name = "Alex Morgan",
            Email = "alex@example.test",
            AccessToken = "test-token"
        });
        await Auth.InitialiseAsync();
    }

    protected void AddSession(int minutes, DateTimeOffset? startedAt = null)
    {
        var start = startedAt ?? DateTimeOffset.UtcNow;
        Store.Data.Sessions.Add(new()
        {
            TaskId = PlannedTask.Id,
            ProjectId = Project.Id,
            StartedAt = start,
            EndedAt = start.AddMinutes(minutes),
            FocusMinutes = minutes
        });
    }

    protected static string Content(AngleSharp.Dom.IElement element) =>
        System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();
}

public sealed class TestApiHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }
    public List<string> Paths { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Add(path);

        if (Respond is not null)
        {
            return Respond(request, cancellationToken);
        }

        object? body = path switch
        {
            "/api/v1/billing/prices" => new BillingPrice[] { new("month", "gbp", 1000, "£10 / month"), new("year", "gbp", 9600, "£96 / year") },
            "/api/v1/billing" => new BillingSummary("Pro", "active", true, null, 2, 100),
            "/api/v1/billing/trial" => new BillingActionResponse(true, null, null, "Trial started"),
            "/api/v1/billing/checkout" => new BillingActionResponse(false, null, null, "Checkout unavailable"),
            "/api/v1/billing/portal" => new BillingActionResponse(false, null, null, "Portal unavailable"),
            _ => null
        };

        return Task.FromResult(body is null
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body, body.GetType()) });
    }
}
