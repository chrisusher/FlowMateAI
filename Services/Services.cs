extern alias Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using ChrisUsher.Core.Services.Interfaces;
using ChrisUsher.Core.Services.Storage;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Billing;
using Services.Coach;
using Services.Database;
using Services.Mcp;
using Services.Reporting;
using Services.Repositories;
using Services.Workspaces;
using Shared.Config;

namespace Services;

public sealed record CosmosDatabaseSettings(string AccountEndpoint, string AccountKey, string DatabaseName);

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

        services.AddDatabase(configuration);
        services.AddSingleton(sp =>
        {
            var settings = ResolveDatabaseSettings(configuration, globalConfig.Environment);

            return new CosmosClient(settings.AccountEndpoint, settings.AccountKey);
        });
        services.AddScoped<IWorkspaceReportRepository, WorkspaceReportRepository>();
        services.AddScoped<IWorkspaceReportService, WorkspaceReportService>();
        services.AddScoped<IMcpCredentialRepository, McpCredentialRepository>();
        services.AddScoped<IMcpCredentialService, McpCredentialService>();
        services.AddScoped<IUserDataResetRepository, UserDataResetRepository>();
        services.AddScoped<IUserDataResetService, UserDataResetService>();

        if (Uri.TryCreate(configuration["KeyVault:VaultUri"] ?? configuration["KeyVault__VaultUri"] ?? configuration["FLOWMATE_SECRETS_URI"], UriKind.Absolute, out var vaultUri))
        {
            services.AddSingleton(new SecretClient(vaultUri, new Identity::Azure.Identity.DefaultAzureCredential()));
        }

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

    public static IServiceCollection AddMcpServices(this IServiceCollection services, IConfiguration configuration)
    {
        var global = configuration.GetSection("Global").Get<GlobalConfig>() ?? new GlobalConfig();
        var settings = ResolveDatabaseSettings(configuration, global.Environment);
        var client = string.IsNullOrWhiteSpace(settings.AccountKey)
            ? new CosmosClient(settings.AccountEndpoint, new Identity::Azure.Identity.DefaultAzureCredential())
            : new CosmosClient(settings.AccountEndpoint, settings.AccountKey);
        services.AddSingleton(client);
        services.AddScoped<IWorkspaceReportRepository, WorkspaceReportRepository>();
        services.AddScoped<IWorkspaceReportService, WorkspaceReportService>();
        services.AddScoped<IMcpCredentialRepository, McpCredentialRepository>();
        services.AddScoped<IMcpCredentialService, McpCredentialService>();

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

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var globalConfig = configuration.GetSection("Global").Get<GlobalConfig>() ?? new GlobalConfig();
        var settings = ResolveDatabaseSettings(configuration, globalConfig.Environment);
        services.AddDbContext<DatabaseContext>(options =>
        {
            Console.WriteLine($"[FlowMate] Using Cosmos endpoint '{settings.AccountEndpoint}' and database '{settings.DatabaseName}'.");
            options.UseCosmos(settings.AccountEndpoint, settings.AccountKey, settings.DatabaseName);

#if DEBUG
            options.EnableDetailedErrors();
            options.LogTo(Console.WriteLine, LogLevel.Information);
#endif
        });

        return services;
    }

    public static CosmosDatabaseSettings ResolveDatabaseSettings(IConfiguration configuration, string? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var cosmosConnection = configuration.GetConnectionString("database");
        var globalEnvironment = environment
            ?? configuration["Global:Environment"]
            ?? configuration["Global__Environment"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Development";
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
        var databaseName = ResolveCosmosDatabaseName(configuration, globalEnvironment);

        return new CosmosDatabaseSettings(accountEndpoint, accountKey, databaseName);
    }

    private static string? ConnectionValue(string? connectionString, string name)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');

            if (separator > 0 && part[..separator].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return part[(separator + 1)..];
            }
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
