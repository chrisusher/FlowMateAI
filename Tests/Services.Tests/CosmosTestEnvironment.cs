using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Testcontainers.CosmosDb;
using CosmosDatabase = Microsoft.Azure.Cosmos.Database;

namespace Services.Tests;

[SetUpFixture]
public sealed class CosmosTestSetup
{
    [OneTimeSetUp]
    public Task StartCosmosEmulatorAsync() => CosmosTestEnvironment.StartAsync();

    [OneTimeTearDown]
    public Task StopCosmosEmulatorAsync() => CosmosTestEnvironment.DisposeAsync().AsTask();
}

internal static class CosmosTestEnvironment
{
    private const string EmulatorImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview";
    private static CosmosDbContainer? _container;
    private static CosmosClient? _client;

    public static async Task StartAsync()
    {
        try
        {
            _container = new CosmosDbBuilder(EmulatorImage).Build();
            await _container.StartAsync();

            _client = new CosmosClient(
                _container.GetConnectionString(),
                new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Gateway,
                    HttpClientFactory = () => _container.HttpClient,
                    RequestTimeout = TimeSpan.FromSeconds(30)
                });
        }
        catch (Exception exception)
        {
            await DisposeAsync();

            throw new InvalidOperationException(
                "Services.Tests requires Docker to start the Cosmos DB emulator. " +
                "Install Docker Desktop or start a Docker-compatible runtime before running the tests.",
                exception);
        }
    }

    public static async Task<CosmosTestDatabase> CreateDatabaseAsync()
    {
        var client = _client ?? throw new InvalidOperationException("The Cosmos DB emulator has not been started.");
        var databaseName = $"services-tests-{Guid.NewGuid():N}";
        var response = await client.CreateDatabaseAsync(databaseName);

        var connectionString = _container?.GetConnectionString()
            ?? throw new InvalidOperationException("The Cosmos DB emulator has not been started.");

        return new CosmosTestDatabase(connectionString, response.Database, databaseName);
    }

    public static HttpClient CreateHttpClient() => _container?.HttpClient
        ?? throw new InvalidOperationException("The Cosmos DB emulator has not been started.");

    public static async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        _client = null;

        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }
}

internal sealed class CosmosTestDatabase(string connectionString, CosmosDatabase database, string name) : IAsyncDisposable
{
    public DbContextOptions<DatabaseContext> CreateOptions() => new DbContextOptionsBuilder<DatabaseContext>()
        .UseCosmos(
            connectionString,
            name,
            cosmos => cosmos
                .ConnectionMode(ConnectionMode.Gateway)
                .HttpClientFactory(CosmosTestEnvironment.CreateHttpClient)
                .RequestTimeout(TimeSpan.FromSeconds(30)))
        .Options;

    public DatabaseContext CreateContext() => new(CreateOptions());

    public async Task<DatabaseContext> CreateContextAsync()
    {
        var context = CreateContext();
        await context.Database.EnsureCreatedAsync();

        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await database.DeleteAsync();
    }
}
