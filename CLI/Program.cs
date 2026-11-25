using CLI.Commands;
using CLI.Commands.User;
using DotNetEnv;
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
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        var environment = builder.Configuration["Global:Environment"];
        var repositoryRoot = FindRepositoryRoot();

        if (!string.IsNullOrWhiteSpace(environment) && repositoryRoot is not null &&
            environment != "." && environment != ".." &&
            environment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            !environment.Contains('/') && !environment.Contains('\\'))
        {
            var path = Path.Combine(repositoryRoot, $".env.{environment}");

            if (File.Exists(path))
            {
                var values = Env.NoEnvVars().Load(path)
                    .GroupBy(pair => pair.Key.Replace("__", ":"), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => (string?)group.Last().Value, StringComparer.OrdinalIgnoreCase);
                builder.Configuration.AddInMemoryCollection(values);
            }
        }

        builder.Configuration
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<CliSettingsMarker>(optional: true)
            .AddEnvironmentVariables();

        builder.AddServiceDefaults();
        builder.Services.AddServices(builder.Configuration);
        builder.Services.AddHttpClient<Auth0ManagementClient>();

        return builder.Build();
    }

    private static string? FindRepositoryRoot()
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

    internal static CancellationTokenSource LinkCancellation(IHost host, CancellationToken commandToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
}

internal sealed class CliSettingsMarker { }
