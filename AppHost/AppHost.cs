using Aspire.Hosting.Foundry;
using DotNetEnv;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using Shared.Enums;

var repositoryRoot = FindRepositoryRoot();

if (repositoryRoot is not null)
{
    var dotEnvPath = Path.Combine(repositoryRoot, ".env");

    if (File.Exists(dotEnvPath))
    {
        Env.Load(dotEnvPath);
    }
}

var builder = DistributedApplication.CreateBuilder(args);

builder.Environment.ApplicationName = "FlowMate AI";

string? LocalSecret(params string[] names)
{
    foreach (var name in names)
    {
        var value = builder.Configuration[name] ?? Environment.GetEnvironmentVariable(name);

        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

// Parameters
var environmentName = builder.Configuration["Parameters:environment"] ?? "Local";

if (!Enum.TryParse<AppHostEnvironment>(environmentName, true, out var selectedEnvironment) ||
    !Enum.IsDefined(selectedEnvironment) ||
    !string.Equals(environmentName, selectedEnvironment.ToString(), StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("The environment parameter must be Local or Test.");
}

builder.Configuration["Parameters:environment"] = selectedEnvironment.ToString();

var auth0Authority = builder.AddParameter("auth0-authority");
var auth0ClientId = builder.AddParameter("auth0-client-id");
var auth0Audience = builder.AddParameter("auth0-audience");

var cache = builder.AddRedis("cache");

// Storage
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite =>
    {
        azurite.WithDataVolume("data");

        azurite.WithBlobPort(10000);
        azurite.WithQueuePort(10001);
        azurite.WithTablePort(10002);

        azurite.WithEndpoint("blob", endpoint => endpoint.IsProxied = false);
        azurite.WithEndpoint("queue", endpoint => endpoint.IsProxied = false);
        azurite.WithEndpoint("table", endpoint => endpoint.IsProxied = false);
    });

var mcpHostStorage = builder.AddAzureStorage("mcp-host-storage")
    .RunAsEmulator(azurite => azurite.WithDataVolume("mcp-host-data"));

var blobs = storage.AddBlobs("blobs");
storage.AddQueues("queues");
storage.AddTables("tables");

// Cosmos DB
var cosmosDb = builder.AddAzureCosmosDB("cosmosDb");
IResourceBuilder<ParameterResource>? testCosmosKey = null;

if (selectedEnvironment == AppHostEnvironment.Test)
{
    var cosmosName = LocalSecret("COSMOS_NAME");
    var cosmosResourceGroup = LocalSecret("COSMOS_RESOURCE_GROUP");

    if (string.IsNullOrWhiteSpace(cosmosName) || string.IsNullOrWhiteSpace(cosmosResourceGroup))
    {
        throw new InvalidOperationException("Set COSMOS_NAME and COSMOS_RESOURCE_GROUP in .env for the Test environment.");
    }

    var cosmosKey = LocalSecret("Database:Key", "Database__Key");

    if (string.IsNullOrWhiteSpace(cosmosKey))
    {
        throw new InvalidOperationException("Set Database__Key in .env for the Test environment.");
    }

    builder.Configuration["Parameters:test-cosmos-key"] = cosmosKey;
    testCosmosKey = builder.AddParameter("test-cosmos-key", secret: true);
    cosmosDb.RunAsExisting(cosmosName, cosmosResourceGroup);
}
else
{
    cosmosDb.WithAccessKeyAuthentication();
}

var cosmosAccountEndpoint = selectedEnvironment == AppHostEnvironment.Local
    ? builder.Configuration["Database:AccountEndpoint"]
    : null;

var databaseName = selectedEnvironment == AppHostEnvironment.Test ? "flowmate-Test" : "flowmate";
var database = cosmosDb.AddCosmosDatabase("database", databaseName);

// Key Vault
var keyVault = builder.AddAzureKeyVault("flowmate-secrets");

var apiSecretReferences = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["Stripe__SecretKey"] = AddVaultSecret("stripe-secret-key", LocalSecret("STRIPE_SECRET_KEY", "Stripe:SecretKey")),
    ["Stripe__WebhookSecret"] = AddVaultSecret("stripe-webhook-secret", LocalSecret("STRIPE_WEBHOOK_SECRET", "Stripe:WebhookSecret")),
    ["Stripe__MonthlyPriceId"] = AddVaultSecret("stripe-monthly-price-id", LocalSecret("STRIPE_MONTHLY_PRICE_ID", "Stripe:MonthlyPriceId")),
    ["Stripe__AnnualPriceId"] = AddVaultSecret("stripe-annual-price-id", LocalSecret("STRIPE_ANNUAL_PRICE_ID", "Stripe:AnnualPriceId")),
    ["Auth0__ManagementClientId"] = AddVaultSecret("auth0-management-client-id", LocalSecret("Auth0:ManagementClientId")),
    ["Auth0__ManagementClientSecret"] = AddVaultSecret("auth0-management-client-secret", LocalSecret("Auth0:ManagementClientSecret"))
};

string AddVaultSecret(string vaultSecretName, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var parameterName = $"{vaultSecretName}-value";
    builder.Configuration[$"Parameters:{parameterName}"] = value;
    var parameter = builder.AddParameter(parameterName, secret: true);
    keyVault.AddSecret(vaultSecretName, parameter);

    return vaultSecretName;
}

// Service Bus
var serviceBus = builder.AddAzureServiceBus("flowmate-service-bus");

var dashboardOtlpEndpoint = builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"];
var otlpProtocol = string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]) ? "grpc" : builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];

// Foundry
var foundry = builder.AddFoundry("flowmate-ai");
var foundryProject = foundry.AddProject("flowmate-ai-project");
var luna = foundry.AddDeployment("gpt56-luna", FoundryModel.OpenAI.Gpt56Luna);

var api = builder.AddAzureFunctionsProject("API", "../API/API.csproj")
    .WaitFor(storage)
    .WaitFor(blobs)
    .WaitFor(database)
    .WithHostStorage(storage)
    .WithEnvironment("ConnectionStrings__Storage", blobs.Resource.ConnectionStringExpression)
    .WithEnvironment("ConnectionStrings__database", database.Resource.ConnectionStringExpression)
    .WithEnvironment("Database__DatabaseName", database.Resource.DatabaseName)
    .WithEnvironment("Global__Environment", environmentName)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", dashboardOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", otlpProtocol)
    .WithHttpHealthCheck("/api/health")
    .WithReference(blobs)
    .WithReference(cache)
    .WithReference(keyVault)
    .WithReference(database)
    .WithReference(serviceBus)
    .WithReference(foundryProject)
    .WithExternalHttpEndpoints();

foreach (var (configurationKey, vaultSecretName) in apiSecretReferences)
{
    if (!string.IsNullOrWhiteSpace(vaultSecretName))
    {
        api.WithEnvironment(configurationKey, keyVault.GetSecret(vaultSecretName));
    }
}

var mcp = builder.AddAzureFunctionsProject("MCP", "../MCP/MCP.csproj")
    .WaitFor(mcpHostStorage)
    .WaitFor(database)
    .WithHostStorage(mcpHostStorage)
    .WithEnvironment("ConnectionStrings__database", database.Resource.ConnectionStringExpression)
    .WithEnvironment("Database__DatabaseName", database.Resource.DatabaseName)
    .WithEnvironment("Global__Environment", environmentName)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", dashboardOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", otlpProtocol)
    .WithReference(database)
    .WithHttpHealthCheck("/api/health")
    .WithExternalHttpEndpoints();

if (!string.IsNullOrWhiteSpace(cosmosAccountEndpoint))
{
    api.WithEnvironment("Database__AccountEndpoint", cosmosAccountEndpoint);
    mcp.WithEnvironment("Database__AccountEndpoint", cosmosAccountEndpoint);
}

if (selectedEnvironment == AppHostEnvironment.Test)
{
    api.WithEnvironment("Database__AccountEndpoint", cosmosDb.Resource.UriExpression)
        .WithEnvironment("Database__Key", testCosmosKey!);
    mcp.WithEnvironment("Database__AccountEndpoint", cosmosDb.Resource.UriExpression)
        .WithEnvironment("Database__Key", testCosmosKey!);
}

var cli = builder.AddProject<Projects.CLI>("cli")
    .WithArgs("--help")
    .WithReference(database)
    .WithReference(blobs)
    .WithEnvironment("Database__DatabaseName", database.Resource.DatabaseName)
    .WithEnvironment("Global__Environment", environmentName)
    .WithExplicitStart()
    .ExcludeFromManifest();

if (selectedEnvironment == AppHostEnvironment.Test)
{
    cli.WithEnvironment("Database__AccountEndpoint", cosmosDb.Resource.UriExpression)
        .WithEnvironment("Database__Key", testCosmosKey!);
}

var buildConfiguration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Name ?? "Debug";
var cliAssemblyPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "CLI", "bin", buildConfiguration, "net11.0", "CLI.dll"));

var cliWorkingDirectory = Path.GetDirectoryName(cliAssemblyPath)!;
var auth0ManagementClientId = builder.Configuration["Auth0:ManagementClientId"];
var auth0ClientSecret = builder.Configuration["Auth0:ManagementClientSecret"];

#pragma warning disable ASPIREPROCESSCOMMAND001
IReadOnlyDictionary<string, string> BuildCliEnvironment(
    string? cosmosConnection,
    string? storageConnection,
    string? keyVaultUri,
    string? runtimeEnvironment,
    string? auth0Issuer,
    bool includeStorageSettings,
    bool includeAuth0Settings,
    bool includeKeyVaultSettings)
{
    var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ConnectionStrings__database"] = cosmosConnection ?? string.Empty,
        ["Database__DatabaseName"] = database.Resource.DatabaseName,
        ["Global__Environment"] = runtimeEnvironment ?? builder.Environment.EnvironmentName,
        ["DOTNET_ENVIRONMENT"] = runtimeEnvironment ?? builder.Environment.EnvironmentName
    };

    if (includeStorageSettings && !string.IsNullOrWhiteSpace(storageConnection))
    {
        environmentVariables["ConnectionStrings__Storage"] = storageConnection;
    }

    if (includeKeyVaultSettings && !string.IsNullOrWhiteSpace(keyVaultUri))
    {
        environmentVariables["KeyVault__VaultUri"] = keyVaultUri;
    }

    if (includeAuth0Settings)
    {
        if (!string.IsNullOrWhiteSpace(auth0Issuer))
        {
            environmentVariables["Auth0__Authority"] = auth0Issuer;
        }

        if (!string.IsNullOrWhiteSpace(auth0ManagementClientId))
        {
            environmentVariables["Auth0__ManagementClientId"] = auth0ManagementClientId;
        }

        if (!string.IsNullOrWhiteSpace(auth0ClientSecret))
        {
            environmentVariables["Auth0__ManagementClientSecret"] = auth0ClientSecret;
        }
    }

    return environmentVariables;
}

async ValueTask<IReadOnlyDictionary<string, string>> BuildCliEnvironmentAsync(
    CancellationToken cancellationToken,
    bool includeStorageSettings,
    bool includeAuth0Settings,
    bool includeKeyVaultSettings = false)
{
    var cosmosConnection = await database.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);

    var storageConnection = includeStorageSettings
        ? await blobs.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken)
        : null;

    var keyVaultUri = includeKeyVaultSettings
        ? await keyVault.Resource.UriExpression.GetValueAsync(cancellationToken)
        : null;

    var auth0Issuer = includeAuth0Settings
        ? await auth0Authority.Resource.GetValueAsync(cancellationToken)
        : null;

    var variables = new Dictionary<string, string>(BuildCliEnvironment(cosmosConnection, storageConnection, keyVaultUri, environmentName, auth0Issuer, includeStorageSettings, includeAuth0Settings, includeKeyVaultSettings), StringComparer.OrdinalIgnoreCase);

    if (selectedEnvironment == AppHostEnvironment.Test)
    {
        variables["Database__AccountEndpoint"] = await cosmosDb.Resource.UriExpression.GetValueAsync(cancellationToken) ?? throw new InvalidOperationException("The Cosmos account endpoint is unavailable.");
        variables["Database__Key"] = await testCosmosKey!.Resource.GetValueAsync(cancellationToken) ?? throw new InvalidOperationException("The Cosmos account key is unavailable.");
    }

    return variables;
}

cli.WithProcessCommand(
    commandName: "ensure-database-created",
    displayName: "Ensure database created",
    processSpecFactory: async context =>
    {
        if (!File.Exists(cliAssemblyPath))
        {
            throw new FileNotFoundException("Build the CLI project before invoking its Aspire command.", cliAssemblyPath);
        }

        var variables = await BuildCliEnvironmentAsync(context.CancellationToken, includeStorageSettings: false, includeAuth0Settings: false);

        var spec = new ProcessCommandSpec("dotnet")
        {
            Arguments = [cliAssemblyPath, "database", "ensure-created"],
            WorkingDirectory = cliWorkingDirectory,
            InheritEnvironmentVariables = false
        };

        foreach (var variable in variables)
        {
            spec.EnvironmentVariables[variable.Key] = variable.Value;
        }

        return spec;
    });

cli.WithProcessCommand(
    commandName: "create-pro-account",
    displayName: "Create new Pro account",
    processSpecFactory: async context =>
    {
        if (!File.Exists(cliAssemblyPath))
        {
            throw new FileNotFoundException("Build the CLI project before invoking its Aspire command.", cliAssemblyPath);
        }

        var email = context.Arguments.GetString("email");

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("An email address is required.");
        }

        var variables = await BuildCliEnvironmentAsync(context.CancellationToken, includeStorageSettings: false, includeAuth0Settings: true);

        var arguments = new List<string>
        {
            cliAssemblyPath,
            "user",
            "grant-pro",
            "--email",
            email
        };
        var userId = context.Arguments.GetString("user-id");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            arguments.AddRange(["--user-id", userId]);
        }

        var spec = new ProcessCommandSpec("dotnet")
        {
            Arguments = arguments,
            WorkingDirectory = cliWorkingDirectory,
            InheritEnvironmentVariables = false
        };

        foreach (var variable in variables)
        {
            spec.EnvironmentVariables[variable.Key] = variable.Value;
        }

        return spec;
    },
    commandOptions: new ProcessCommandOptions
    {
        Arguments =
        [
            new InteractionInput
            {
                Name = "email",
                Label = "Email address", InputType = InputType.Text,
                Required = true
            },
            new InteractionInput
            {
                Name = "user-id",
                Label = "Auth0 user ID (when needed)",
                InputType = InputType.Text
            }
        ]
    });

cli.WithProcessCommand(
    commandName: "reset-user-data",
    displayName: "Reset user data",
    processSpecFactory: async context =>
    {
        if (!File.Exists(cliAssemblyPath))
        {
            throw new FileNotFoundException("Build the CLI project before invoking its Aspire command.", cliAssemblyPath);
        }

        var email = context.Arguments.GetString("email");

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("An email address is required.");
        }

        if (!bool.TryParse(context.Arguments.GetString("approve"), out var approve) || !approve)
        {
            throw new InvalidOperationException("Approval is required before the reset process can start. Set approve to true.");
        }

        var variables = await BuildCliEnvironmentAsync(context.CancellationToken, includeStorageSettings: true, includeAuth0Settings: true, includeKeyVaultSettings: true);

        var arguments = new List<string>
        {
            cliAssemblyPath,
            "user",
            "reset",
            "--email",
            email, "--approve"
        };
        var userId = context.Arguments.GetString("user-id");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            arguments.AddRange(["--user-id", userId]);
        }

        var spec = new ProcessCommandSpec("dotnet")
        {
            Arguments = arguments,
            WorkingDirectory = cliWorkingDirectory,
            InheritEnvironmentVariables = false
        };

        foreach (var variable in variables)
        {
            spec.EnvironmentVariables[variable.Key] = variable.Value;
        }

        return spec;
    },
    commandOptions: new ProcessCommandOptions
    {
        Arguments =
        [
            new InteractionInput
            {
                Name = "email",
                Label = "Email",
                InputType = InputType.Text,
                Required = true
            },
            new InteractionInput
            {
                Name = "user-id",
                Label = "Auth0 user ID (when needed)",
                InputType = InputType.Text
            },
            new InteractionInput
            {
                Name = "approve",
                Label = "Approve deletion",
                InputType = InputType.Boolean,
                Value = "false"
            }
        ]
    });
#pragma warning restore ASPIREPROCESSCOMMAND001

var frontend = builder.AddBlazorWasmApp("frontend", "../Web/Web.csproj")
    .WithReference(api)
    .WithEnvironment("ApiBaseUrl", api.GetEndpoint("http"))
    .WithEnvironment("McpEndpoint", mcp.GetEndpoint("http"));

api.WithEnvironment("Auth0__Authority", auth0Authority)
    .WithEnvironment("Auth0__Audience", auth0Audience);

var gateway = builder.AddBlazorGateway("frontend-gateway")
    .WaitFor(api)
    .WithEndpoint("http", endpoint => endpoint.Port = builder.Configuration.GetValue<int?>("Frontend:HttpPort"))
    .WithEndpoint("https", endpoint => endpoint.Port = builder.Configuration.GetValue<int?>("Frontend:HttpsPort"))
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/frontend/")
    .WithBlazorClientApp(frontend);

var marketing = builder.AddBlazorWasmApp("marketing", "../Marketing/Marketing.csproj");

builder.AddBlazorGateway("marketing-gateway")
    .WithExternalHttpEndpoints()
    .WithBlazorClientApp(marketing)
    .WithEnvironment(context =>
    {
        // The Blazor gateway only forwards service references automatically. Add the
        // public app address to its browser configuration without exposing secrets.
        const string configurationKey = "ClientApps__marketing__ConfigResponse";
        context.EnvironmentVariables.TryGetValue(configurationKey, out var generated);
        context.EnvironmentVariables[configurationKey] = new MarketingClientConfiguration(generated, gateway.GetEndpoint("http"));
    });
// The Blazor gateway generates its own client configuration and does not forward
// arbitrary WithEnvironment values from the WASM resource. Add only public SPA
// settings to its generated response; never include management credentials.
if (builder.ExecutionContext.IsRunMode)
{
    gateway.WithEnvironment(context =>
    {
        const string key = "ClientApps__frontend__ConfigResponse";
        context.EnvironmentVariables[key] = new FrontendClientConfiguration(
            context.EnvironmentVariables[key], auth0Authority.Resource, auth0ClientId.Resource, auth0Audience.Resource, mcp.GetEndpoint("http"));
    });
}

api.WithEnvironment("Cors__AllowedOrigins__0", gateway.GetEndpoint("http"))
    .WithEnvironment("WebApp__Origin", gateway.GetEndpoint("https"))
    .WithEnvironment("WebApp__BasePath", "/frontend");

builder.Build().Run();

static string? FindRepositoryRoot()
{
    foreach (var startingPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (var directory = new DirectoryInfo(startingPath); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FlowMateAI.slnx")))
            {
                return directory.FullName;
            }
        }
    }

    return null;
}

internal sealed class FrontendClientConfiguration(object? generated, IValueProvider authority, IValueProvider clientId, IValueProvider audience, IValueProvider mcpEndpoint) : IValueProvider
{
    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        var json = generated is IValueProvider provider
            ? await provider.GetValueAsync(cancellationToken)
            : generated?.ToString();

        var configuration = JsonNode.Parse(json!)!.AsObject();
        var clientEnvironment = configuration["webAssembly"]!["environment"]!.AsObject();

        clientEnvironment["Auth0__Domain"] = await authority.GetValueAsync(cancellationToken);
        clientEnvironment["Auth0__ClientId"] = await clientId.GetValueAsync(cancellationToken);
        clientEnvironment["Auth0__Audience"] = await audience.GetValueAsync(cancellationToken);
        var endpoint = await mcpEndpoint.GetValueAsync(cancellationToken);

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            // Expose the loopback endpoint that the settings UI accepts for local HTTP.
            endpoint = new UriBuilder(uri)
            {
                Host = "localhost"
            }.Uri.ToString();
        }

        clientEnvironment["McpEndpoint"] = endpoint;

        return configuration.ToJsonString();
    }
}

internal sealed class MarketingClientConfiguration(object? generated, IValueProvider appEndpoint) : IValueProvider
{
    public async ValueTask<string?> GetValueAsync(CancellationToken cancellationToken = default)
    {
        var json = generated is IValueProvider provider
            ? await provider.GetValueAsync(cancellationToken)
            : generated?.ToString();

        var configuration = JsonNode.Parse(json ?? "{}")!.AsObject();
        var webAssembly = configuration["webAssembly"] as JsonObject ?? new();
        var environment = webAssembly["environment"] as JsonObject ?? new();

        environment["AppUrl"] = $"{await appEndpoint.GetValueAsync(cancellationToken)}/frontend/";
        webAssembly["environment"] = environment.DeepClone();
        configuration["webAssembly"] = webAssembly.DeepClone();

        return configuration.ToJsonString();
    }
}

