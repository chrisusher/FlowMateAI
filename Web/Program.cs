using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Radzen;
using Web.Clients;
using Web.Components;
using Web.Managers;

namespace Web;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);

        // Aspire's Blazor gateway supplies browser-safe values before the WASM runtime starts.
        // This has precedence over appsettings.Development.json for locally orchestrated runs.
        EnvironmentVariablesExtensions.AddEnvironmentVariables(
            (IConfigurationBuilder)builder.Configuration);

        // The gateway exposes referenced services as browser-safe service-discovery values.
        // Preserve the configured fallback outside Aspire, but prefer the same-origin proxy
        // while the app is orchestrated locally.
        var backendApiBaseUrl = builder.Configuration["services:API:https:0"]
            ?? builder.Configuration["services:API:http:0"]
            ?? builder.Configuration["ApiBaseUrl"];

        if (string.IsNullOrWhiteSpace(backendApiBaseUrl))
        {
            throw new InvalidOperationException("The API endpoint is missing from the gateway configuration.");
        }

        builder.Configuration["ApiBaseUrl"] = backendApiBaseUrl;

        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        // Retain the gateway proxy prefix when resolving relative API routes.
        builder.Services.AddScoped(sp => new HttpClient
        {
            BaseAddress = new Uri(new Uri(builder.HostEnvironment.BaseAddress),
                backendApiBaseUrl.TrimEnd('/') + "/")
        });

        // API clients
        builder.Services.AddScoped<AuthClient>();
        builder.Services.AddAuthorizationCore();
        builder.Services.AddScoped<AuthenticationStateProvider, OidcAuthenticationStateProvider>();
        builder.Services.AddScoped<BillingClient>();
        builder.Services.AddScoped<McpKeysClient>();
        builder.Services.AddScoped<WorkspaceStore>();

        builder.Services.AddScoped<WorkspaceStatistics>();
        builder.Services.AddScoped<WorkspaceTaskManager>();
        builder.Services.AddScoped<WorkspaceProjectManager>();
        builder.Services.AddScoped<WorkspaceTimerManager>();
        builder.Services.AddScoped<WorkspaceBillingManager>();
        builder.Services.AddScoped<WorkspaceCoachManager>();
        builder.Services.AddScoped<WorkspaceAppearanceManager>();

        #region Managers

        // Add theme manager
        builder.Services.AddScoped<ThemeManager>();

        #endregion

        // Add configuration - this properly loads appsettings.json and environment-specific files
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

        // Add logging
        builder.Services.AddLogging();
        builder.Services.AddRadzenComponents();

        var host = builder.Build();

        await host.RunAsync();
    }
}
