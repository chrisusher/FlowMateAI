using Azure.Storage.Blobs;
using ChrisUsher.Core.Services.Interfaces;
using ChrisUsher.Core.Services.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Database;
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
                .WithName("AppTemplate");

            // Add KeyVault
            // var keyVaultConfig = configuration
            //     .GetSection("KeyVault")
            //     .Get<KeyVaultConfig>();

            // config.AddSecretClient(new Uri(configuration["KEYVAULT_URI"] ?? throw new InvalidOperationException("KEYVAULT_URI is not set in configuration")));
        });

        #endregion

        var functionsConfig = configuration
            .GetSection("Functions")
            .Get<FunctionsConfig>() ?? new FunctionsConfig();

        var globalConfig = configuration
            .GetSection("Global")
            .Get<GlobalConfig>() ?? new GlobalConfig();

//         services.AddDbContext<DatabaseContext>(options =>
//         {
//             var accountEndpoint = NormaliseCosmosAccountEndpoint(configuration["Database:AccountName"] ?? configuration["Database__AccountName"] ?? string.Empty);
//             var accountKey = configuration["Database:Key"] ?? configuration["Database__Key"] ?? string.Empty;

//             var databaseName = ResolveCosmosDatabaseName(configuration, globalConfig.Environment);

//             Console.WriteLine($"[AppTemplate] Using Cosmos endpoint '{accountEndpoint}' and database '{databaseName}'.");

//             options.UseCosmos(
//                 accountEndpoint,
//                 accountKey,
//                 databaseName
//             );

//             options.EnableSensitiveDataLogging();

// #if DEBUG
//             options.EnableDetailedErrors();
//             options.LogTo(Console.WriteLine, LogLevel.Information);
// #endif
//         });

        services.AddTransient<IStorageService>(services =>
        {
            var blobServiceClient = services.GetRequiredService<BlobServiceClient>();
            return new BlobStorageService(blobServiceClient);
        });

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

        services.AddSingleton(s => services.BuildServiceProvider());

        return services;
    }

    internal static string ResolveCosmosDatabaseName(IConfiguration configuration, string environment)
    {
        var configuredDatabaseName = configuration["Database:DatabaseName"] ?? configuration["Database__DatabaseName"];

        if (!string.IsNullOrWhiteSpace(configuredDatabaseName))
        {
            return configuredDatabaseName;
        }

        return $"StockTraderAgent-{environment ?? "Development"}";
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
