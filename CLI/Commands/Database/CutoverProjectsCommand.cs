using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Repositories;

namespace CLI.Commands.Database;

internal static class CutoverProjectsCommand
{
    public static Command Create()
    {
        var command = new Command("cutover-projects", "Preview or cut over one account's project records. Stop every workspace writer first.");
        var user = new Option<string>("--user-id")
        {
            Required = true,
            Description = "Exact account user ID / Cosmos partition key."
        };
        var apply = new Option<bool>("--apply")
        {
            Description = "Apply the cutover after preview and backup."
        };
        var backup = new Option<string?>("--backup")
        {
            Description = "New local backup file, required with --apply."
        };
        var stopped = new Option<bool>("--writers-stopped")
        {
            Description = "Confirm all old and new workspace writers are stopped for the duration."
        };
        command.Options.Add(user);
        command.Options.Add(apply);
        command.Options.Add(backup);
        command.Options.Add(stopped);
        command.SetAction(async (result, token) => await ExecuteAsync(result.GetValue(user), result.GetValue(apply), result.GetValue(backup), result.GetValue(stopped), token));

        return command;
    }

    private static async Task<int> ExecuteAsync(string? userId, bool apply, string? backup, bool writersStopped, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(userId) || (apply && (string.IsNullOrWhiteSpace(backup) || !writersStopped)))
        {
            Console.Error.WriteLine("Supply an account user ID; applying also requires a new --backup path and --writers-stopped.");

            return 2;
        }

        try
        {
            using var host = Program.CreateHost();
            await host.StartAsync(token);
            using var cancellation = Program.LinkCancellation(host, token);
            var ct = cancellation.Token;
            var configuration = host.Services.GetRequiredService<IConfiguration>();

            if (string.IsNullOrWhiteSpace(configuration["Database:DatabaseName"]))
            {
                Console.Error.WriteLine("Set Database:DatabaseName explicitly before project cutover.");

                return 2;
            }

            var settings = Services.Services.ResolveDatabaseSettings(configuration);
            await using var scope = host.Services.CreateAsyncScope();
            var cutover = scope.ServiceProvider.GetRequiredService<ProjectCutoverRepository>();
            var preview = await cutover.PreviewAsync(userId, ct);
            Console.WriteLine($"Cosmos endpoint: {settings.AccountEndpoint}");
            Console.WriteLine($"Database: {settings.DatabaseName}; account: {userId}");
            Console.WriteLine($"Projects: {preview.ProjectCount}; active: {preview.ActiveCount}; source revision: {preview.Revision}");

            if (!apply)
            {
                Console.WriteLine("Preview only. Stop every workspace writer before applying with --apply --writers-stopped --backup.");

                return 0;
            }

            var source = await scope.ServiceProvider.GetRequiredService<IWorkspaceRepository>().GetAsync(userId, ct)
                ?? throw new InvalidOperationException("The workspace was not found.");

            if (!string.Equals(source.ETag, preview.Revision, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The workspace changed after preview.");
            }

            var original = JsonSerializer.SerializeToUtf8Bytes(new ProjectCutoverBackup(settings.AccountEndpoint.ToString(), settings.DatabaseName, userId, source));
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var file = new FileStream(backup!, options))
            {
                await file.WriteAsync(original, ct);
                await file.FlushAsync(ct);
            }

            await cutover.ApplyAsync(userId, preview.Revision, ct);
            Console.WriteLine($"Project cutover complete. Keep the backup at {backup} private and retain it for rollback.");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cutover cancelled. Retain the backup and keep writers stopped while checking the account.");

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Project cutover failed ({exception.GetType().Name}). Retain any backup and keep writers stopped while checking the account.");

            return 1;
        }
    }

    private sealed record ProjectCutoverBackup(string Endpoint, string Database, string UserId, Services.Database.WorkspaceDocument Workspace);
}
