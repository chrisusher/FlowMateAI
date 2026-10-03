using CLI.Commands;
using CLI.Commands.User;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Services;

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
        builder.Services.AddServices(builder.Configuration);
        builder.Services.AddHttpClient<Auth0ManagementClient>();

        return builder.Build();
    }

    internal static CancellationTokenSource LinkCancellation(IHost host, CancellationToken commandToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

internal sealed class CliSettingsMarker { }
