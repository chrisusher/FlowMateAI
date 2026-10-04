using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

builder.Environment.ApplicationName = "FlowMate AI";

// Parameters
var environment = builder.AddParameter("environment", false);

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

// Service Bus
var serviceBus = builder.AddAzureServiceBus("flowmate-service-bus");

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume("keycloak-data")
    .WithRealmImport("../infra/keycloak/realm-import");
    
var keycloakBaseUrl = keycloak.GetEndpoint("http");
var keycloakAuthority = keycloakBaseUrl + "/realms/flowmate";

var dashboardOtlpEndpoint = builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"];
var otlpProtocol = string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]) ? "grpc" : builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];

// Foundry
var foundry = builder.AddFoundry("flowmate-ai");
var foundryProject = foundry.AddProject("flowmate-ai-project");
var luna = foundry.AddDeployment("gpt56-luna", FoundryModel.OpenAI.Gpt56Luna);

var api = builder.AddAzureFunctionsProject("API", "../API/API.csproj")
    .WaitFor(keycloak)
    .WaitFor(storage)
    .WaitFor(blobs)
    .WithHostStorage(storage)
    .WithEnvironment("ConnectionStrings__Storage", blobs.Resource.ConnectionStringExpression)
    .WithEnvironment("Database__DatabaseName", database.Resource.DatabaseName)
    .WithEnvironment("Database__Key", cosmosDb.Resource.AccountKey!)
    .WithEnvironment("Global__Environment", environment)
    .WithEnvironment("Authentication__Authority", keycloakAuthority)
    .WithEnvironment("Authentication__Audience", "flowmate-api")
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
    .WithEnvironment("Keycloak__Url", keycloakBaseUrl)
    .WithEnvironment("Keycloak__Realm", "flowmate")
    .WithEnvironment("Keycloak__ManagementClientId", "flowmate-cli")
    .WithExplicitStart()
    .ExcludeFromManifest();

var buildConfiguration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.Name ?? "Debug";
var cliAssemblyPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "CLI", "bin", buildConfiguration, "net10.0", "CLI.dll"));

var cliWorkingDirectory = Path.GetDirectoryName(cliAssemblyPath)!;
var keycloakUrl = builder.Configuration["Keycloak:Url"];
var keycloakClientId = builder.Configuration["Keycloak:ManagementClientId"];
var keycloakClientSecret = builder.Configuration["Keycloak:ManagementClientSecret"];

#pragma warning disable ASPIREPROCESSCOMMAND001
IReadOnlyDictionary<string, string> BuildCliEnvironment(
    string? cosmosConnection,
    string? storageConnection,
    string? keyVaultUri,
    string? runtimeEnvironment,
    string? configuredKeycloakUrl,
    bool includeStorageSettings,
    bool includeKeycloakSettings,
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

    if (includeKeycloakSettings)
    {
        if (!string.IsNullOrWhiteSpace(configuredKeycloakUrl))
        {
            environmentVariables["Keycloak__Url"] = configuredKeycloakUrl;
        }

        if (!string.IsNullOrWhiteSpace(keycloakClientId))
        {
            environmentVariables["Keycloak__ManagementClientId"] = keycloakClientId;
        }

        if (!string.IsNullOrWhiteSpace(keycloakClientSecret))
        {
            environmentVariables["Keycloak__ManagementClientSecret"] = keycloakClientSecret;
        }

        environmentVariables["Keycloak__Realm"] = "flowmate";
    }

    return environmentVariables;
}

async ValueTask<IReadOnlyDictionary<string, string>> BuildCliEnvironmentAsync(
    CancellationToken cancellationToken,
    bool includeStorageSettings,
    bool includeKeycloakSettings,
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
    var configuredKeycloakUrl = includeKeycloakSettings
        ? keycloakUrl ?? await keycloak.GetEndpoint("http").GetValueAsync(cancellationToken)
        : null;

    return BuildCliEnvironment(cosmosConnection, storageConnection, keyVaultUri, configuredEnvironment, configuredKeycloakUrl, includeStorageSettings, includeKeycloakSettings, includeKeyVaultSettings);
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

        var variables = await BuildCliEnvironmentAsync(context.CancellationToken, includeStorageSettings: false, includeKeycloakSettings: false);

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

        var variables = await BuildCliEnvironmentAsync(context.CancellationToken, includeStorageSettings: false, includeKeycloakSettings: true);

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
                Label = "Keycloak user ID (when needed)",
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

        var variables = await BuildCliEnvironmentAsync(context.CancellationToken, includeStorageSettings: true, includeKeycloakSettings: true, includeKeyVaultSettings: true);

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
                Label = "Keycloak user ID (when needed)",
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
    .WithEnvironment("McpEndpoint", mcp.GetEndpoint("http"))
    .WithEnvironment("Keycloak__Url", keycloakBaseUrl)
    .WithEnvironment("Keycloak__Realm", "flowmate")
    .WithEnvironment("Keycloak__ClientId", "flowmate-web");

var gateway = builder.AddBlazorGateway("frontend-gateway")
    .WaitFor(api)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/frontend/")
    .WithBlazorClientApp(frontend);

api.WithEnvironment("Cors__AllowedOrigins__0", gateway.GetEndpoint("http"));

builder.Build().Run();
