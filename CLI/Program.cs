using CLI.Commands;
using CLI.Commands.User;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services;
using Services.Database;

namespace CLI;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {

        return await CliCommandLine.Create().Parse(args).InvokeAsync();
    }

    internal static IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory
        });

        builder.Configuration.Sources.Clear();
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<CliSettingsMarker>(optional: true)
            .AddEnvironmentVariables();

        builder.AddServiceDefaults();
        builder.Services.AddDatabase(builder.Configuration);
        builder.Services.AddHttpClient<Auth0ManagementClient>();
        builder.Services.AddScoped<IUserDataResetService>(services =>
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var storageConnection = configuration.GetConnectionString("Storage");

            if (string.IsNullOrWhiteSpace(storageConnection))
            {
                throw new InvalidOperationException("ConnectionStrings:Storage is required for user reset.");
            }

            var database = services.GetRequiredService<DatabaseContext>();

            return new UserDataResetService(database, new Azure.Storage.Blobs.BlobServiceClient(storageConnection));
        });

        return builder.Build();
    }

    internal static CancellationTokenSource LinkCancellation(IHost host, CancellationToken commandToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

internal sealed class CliSettingsMarker { }
