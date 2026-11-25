using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Services.Database;
using Testcontainers.CosmosDb;

namespace Services.Tests;

internal static class CosmosTestEnvironment
{
    private const string EmulatorImage = "mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview";
    private static CosmosDbContainer? _container;

    public static async Task StartAsync()
    {
        try
        {
            _container = new CosmosDbBuilder(EmulatorImage).Build();
            await _container.StartAsync();
        }
        catch (Exception exception)
        {
            await DisposeAsync();

            throw new InvalidOperationException(
                "Cosmos-backed tests require Docker to start the Cosmos DB emulator. " +
                "Install Docker Desktop or start a Docker-compatible runtime before running the tests.",
                exception);
        }
    }

    public static async Task<CosmosTestDatabase> CreateDatabaseAsync()
    {
        var connectionString = _container?.GetConnectionString()
            ?? throw new InvalidOperationException("The Cosmos DB emulator has not been started.");
        var database = new CosmosTestDatabase(connectionString, $"services-tests-{Guid.NewGuid():N}");
        await using var context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();

        return database;
    }

    public static HttpClient CreateHttpClient() => _container?.HttpClient
        ?? throw new InvalidOperationException("The Cosmos DB emulator has not been started.");

    public static async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }
}

internal sealed class CosmosTestDatabase(string connectionString, string name) : IAsyncDisposable
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

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
