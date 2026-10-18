using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;
using DotNetEnv;
using Microsoft.Extensions.Configuration;

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
var environment = builder.AddParameter("environment", false);
var auth0Authority = builder.AddParameter("auth0-authority");

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
var cosmosDb = builder.AddAzureCosmosDB("cosmosDb")
.WithAccessKeyAuthentication();

var cosmosAccountEndpoint = builder.Configuration["Database:AccountEndpoint"];

var database = cosmosDb.AddCosmosDatabase("database", "flowmate");

// Key Vault
var keyVault = builder.AddAzureKeyVault("flowmate-secrets");

var stripeSecretReferences = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["Stripe__SecretKey"] = AddStripeVaultSecret("stripe-secret-key", LocalSecret("STRIPE_SECRET_KEY", "Stripe:SecretKey")),
    ["Stripe__WebhookSecret"] = AddStripeVaultSecret("stripe-webhook-secret", LocalSecret("STRIPE_WEBHOOK_SECRET", "Stripe:WebhookSecret")),
    ["Stripe__MonthlyPriceId"] = AddStripeVaultSecret("stripe-monthly-price-id", LocalSecret("STRIPE_MONTHLY_PRICE_ID", "Stripe:MonthlyPriceId")),
    ["Stripe__AnnualPriceId"] = AddStripeVaultSecret("stripe-annual-price-id", LocalSecret("STRIPE_ANNUAL_PRICE_ID", "Stripe:AnnualPriceId"))
};

string AddStripeVaultSecret(string vaultSecretName, string? value)
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
    .WithHostStorage(storage)
    .WithEnvironment("ConnectionStrings__Storage", blobs.Resource.ConnectionStringExpression)
    .WithEnvironment("Database__DatabaseName", database.Resource.DatabaseName)
    .WithEnvironment("Database__Key", cosmosDb.Resource.AccountKey!)
    .WithEnvironment("Global__Environment", environment)
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

foreach (var (configurationKey, vaultSecretName) in stripeSecretReferences)
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
    .WithEnvironment("Database__DatabaseName", database.Resource.DatabaseName)
    .WithEnvironment("Database__Key", cosmosDb.Resource.AccountKey!)
    .WithEnvironment("Global__Environment", environment)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", dashboardOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", otlpProtocol)
    .WithReference(database)
    .WithHttpHealthCheck("/api/health")
    .WithExternalHttpEndpoints();

if (string.IsNullOrWhiteSpace(cosmosAccountEndpoint))
{
    api.WithEnvironment("Database__AccountEndpoint", cosmosDb.Resource.UriExpression);
    mcp.WithEnvironment("Database__AccountEndpoint", cosmosDb.Resource.UriExpression);
}
else
{
    api.WithEnvironment("Database__AccountEndpoint", cosmosAccountEndpoint);
    mcp.WithEnvironment("Database__AccountEndpoint", cosmosAccountEndpoint);
}

var cli = builder.AddProject<Projects.CLI>("cli")
    .WithArgs("--help")
    .WithReference(database)
    .WithReference(blobs)
    .WithEnvironment("Database__DatabaseName", database.Resource.DatabaseName)
    .WithEnvironment("Global__Environment", environment)
    .WithExplicitStart()
    .ExcludeFromManifest();

var buildConfiguration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Name ?? "Debug";
var cliAssemblyPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "CLI", "bin", buildConfiguration, "net10.0", "CLI.dll"));

var cliWorkingDirectory = Path.GetDirectoryName(cliAssemblyPath)!;
var auth0ClientId = builder.Configuration["Auth0:ManagementClientId"];
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

        if (!string.IsNullOrWhiteSpace(auth0ClientId))
        {
            environmentVariables["Auth0__ManagementClientId"] = auth0ClientId;
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

    var configuredEnvironment = await environment.Resource.GetValueAsync(cancellationToken);
    var auth0Issuer = includeAuth0Settings
        ? await auth0Authority.Resource.GetValueAsync(cancellationToken)
        : null;

    return BuildCliEnvironment(cosmosConnection, storageConnection, keyVaultUri, configuredEnvironment, auth0Issuer, includeStorageSettings, includeAuth0Settings, includeKeyVaultSettings);
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
    .WithEnvironment("Auth0__Audience", builder.Configuration["Auth0:Audience"]);

var gateway = builder.AddBlazorGateway("frontend-gateway")
    .WaitFor(api)
    .WithEndpoint("http", endpoint => endpoint.Port = builder.Configuration.GetValue<int?>("Frontend:HttpPort"))
    .WithEndpoint("https", endpoint => endpoint.Port = builder.Configuration.GetValue<int?>("Frontend:HttpsPort"))
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/frontend/")
    .WithBlazorClientApp(frontend);

api.WithEnvironment("Cors__AllowedOrigins__0", gateway.GetEndpoint("http"))
    .WithEnvironment("WebApp__Origin", gateway.GetEndpoint("https"));

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

