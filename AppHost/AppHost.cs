using Aspire.Hosting.Foundry;

var builder = DistributedApplication.CreateBuilder(args);

builder.Environment.ApplicationName = "App-Template";

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

var blobs = storage.AddBlobs("blobs");
storage.AddQueues("queues");
storage.AddTables("tables");

// Cosmos DB
var cosmosDb = builder.AddAzureCosmosDB("cosmosDb");

var database = cosmosDb.AddCosmosDatabase("database", "app-template");
database.AddContainer("weather", "/id", "weather");

// Key Vault
var keyVault = builder.AddAzureKeyVault("app-template-secrets");

// Service Bus
var serviceBus = builder.AddAzureServiceBus("app-template-service-bus");
var weatherQueue = serviceBus.AddServiceBusQueue("weather-queue", "weather-queue");

var dashboardOtlpEndpoint = builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"];
var otlpProtocol = string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"]) ? "grpc" : builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"];

// Foundry
var foundry = builder.AddFoundry("app-template-ai");
var foundryProject = foundry.AddProject("app-template-ai-project");
var luna = foundry.AddDeployment("gpt56-luna", FoundryModel.OpenAI.Gpt56Luna);

var api = builder.AddAzureFunctionsProject("API", "../API/API.csproj")
    .WaitFor(storage)
    .WaitFor(blobs)
    .WithHostStorage(storage)
    .WithEnvironment("ConnectionStrings__Storage", blobs.Resource.ConnectionStringExpression)
    .WithEnvironment("Global__Environment", environment)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", dashboardOtlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", otlpProtocol)
    .WithHttpHealthCheck("/api/health")
    .WithReference(blobs)
    .WithReference(cache)
    .WithReference(keyVault)
    .WithReference(database)
    .WithReference(serviceBus)
    .WithReference(weatherQueue)
    .WithReference(foundryProject)
    .WithExternalHttpEndpoints();

var frontend = builder.AddBlazorWasmApp("frontend", "../Web/Web.csproj")
    .WithReference(api)
    .WithEnvironment("ApiBaseUrl", api.GetEndpoint("http"));

builder.AddBlazorGateway("frontend-gateway")
    .WaitFor(api)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/frontend/")
    .WithBlazorClientApp(frontend);

builder.Build().Run();
