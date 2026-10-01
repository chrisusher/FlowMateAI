using Azure.Storage.Blobs;
using ChrisUsher.Core.Services.Interfaces;
using ChrisUsher.Core.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Database;
using Services.Repositories;
using Services.Workspaces;
using Services.Billing;
using Services.Coach;
using Shared.Config;

namespace Services;

public static class Services
{
    public static IServiceCollection AddServices(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddLogging(logging =>
        {
            // Prevent Azure Storage logs from spamming unless there are errors
            logging.AddFilter("Azure.Storage.Blobs", LogLevel.Error);
            logging.AddFilter("Azure.Storage.Queues", LogLevel.Error);
            logging.AddFilter("Azure.Storage.Common", LogLevel.Error);
            logging.AddFilter("Azure.Storage", LogLevel.Error);
            logging.AddFilter("Azure.Core", LogLevel.Error);
            logging.AddFilter("Azure", LogLevel.Error);
            logging.AddFilter("Microsoft.Azure.Storage", LogLevel.Error);
            logging.AddFilter("Microsoft.Azure.WebJobs.Host.Blobs", LogLevel.Error);
            logging.AddFilter("Microsoft.Azure.WebJobs.Extensions.Storage", LogLevel.Error);

            // Suppress Azure Functions host internal storage operations
            logging.AddFilter((category, level) =>
            {
                // Filter out Azure Storage request/response logs that are Information level or lower
                if (category?.StartsWith("Azure.") == true && level <= LogLevel.Information)
                {
                    return false;
                }
                return true;
            });
        });

        #region Azure Services

        services.AddAzureClients(config =>
        {
            var storageConnectionString = configuration.GetConnectionString("Storage")
                ?? throw new InvalidOperationException("ConnectionStrings:Storage is not set in configuration");

            config.AddBlobServiceClient(storageConnectionString)
                .WithName("FlowMate");

            // Application secrets are supplied through server-side app settings or the host secret store.
        });

        #endregion

        var functionsConfig = configuration
            .GetSection("Functions")
            .Get<FunctionsConfig>() ?? new FunctionsConfig();

        var globalConfig = configuration
            .GetSection("Global")
            .Get<GlobalConfig>() ?? new GlobalConfig();

        services.AddDbContext<DatabaseContext>(options =>
        {
            var cosmosConnection = configuration.GetConnectionString("database");
            var accountEndpoint = NormaliseCosmosAccountEndpoint(
                configuration["Database:AccountEndpoint"]
                    ?? configuration["Database__AccountEndpoint"]
                    ?? configuration["Database:AccountName"]
                    ?? configuration["Database__AccountName"]
                    ?? ConnectionValue(cosmosConnection, "AccountEndpoint")
                    ?? string.Empty);
            var accountKey = configuration["Database:Key"]
                ?? configuration["Database__Key"]
                ?? ConnectionValue(cosmosConnection, "AccountKey")
                ?? string.Empty;

            var databaseName = ResolveCosmosDatabaseName(configuration, globalConfig.Environment);

            Console.WriteLine($"[FlowMate] Using Cosmos endpoint '{accountEndpoint}' and database '{databaseName}'.");

            options.UseCosmos(
                accountEndpoint,
                accountKey,
                databaseName
            );

#if DEBUG
            options.EnableDetailedErrors();
            options.LogTo(Console.WriteLine, LogLevel.Information);
#endif
        });

        services.AddTransient<IStorageService>(services =>
        {
            var blobServiceClient = services.GetRequiredService<BlobServiceClient>();
            return new BlobStorageService(blobServiceClient);
        });

        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IActivityArchiveRepository, BlobActivityArchiveRepository>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();
        services.AddScoped<IBillingRepository, BillingRepository>();
        services.AddScoped<IBillingService, StripeBillingService>();
        services.AddScoped<IFocusCoachService, FoundryFocusCoachService>();

        #region Config
        services.AddSingleton(functionsConfig!);
        services.AddSingleton(globalConfig!);
        #endregion

        #region Repositories

        #endregion

        #region Services

        #region EF Core Services

        #endregion

        #endregion

        #region Clients

        #endregion

        return services;
    }

    internal static string ResolveCosmosDatabaseName(IConfiguration configuration, string environment)
    {
        var configuredDatabaseName = configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"];

        if (!string.IsNullOrWhiteSpace(configuredDatabaseName))
        {
            return configuredDatabaseName;
        }

        return $"flowmate-{environment ?? "Development"}";
    }

    private static string? ConnectionValue(string? connectionString, string name)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator > 0 && part[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
                return part[(separator + 1)..];
        }
        return null;
    }

    internal static string NormaliseCosmosAccountEndpoint(string? accountEndpoint)
    {
        if (string.IsNullOrWhiteSpace(accountEndpoint))
        {
            return string.Empty;
        }

        var trimmed = accountEndpoint.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.TrimEnd('/') + "/";
        }

        if (trimmed.Contains("documents.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{trimmed.Trim('/')}";
        }

        return $"https://{trimmed.Trim('/')}.documents.azure.com:443/";
    }
}
