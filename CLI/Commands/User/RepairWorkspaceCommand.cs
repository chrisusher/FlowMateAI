using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Repositories;
using Shared.Models;

namespace CLI.Commands.User;

internal static class RepairWorkspaceCommand
{
    public static Command Create()
    {
        var command = new Command("repair-workspace", "Preview or repair one account's workspace and historical focus durations.");
        var user = new Option<string>("--user-id") { Required = true, Description = "Exact account user ID / Cosmos partition key." };
        var minutes = new Option<int?>("--planned-minutes") { Description = "Assumed duration for records with no planned duration. Omit to migrate storage only." };
        var apply = new Option<bool>("--apply") { Description = "Apply the changes; without this option the command previews only." };
        var backup = new Option<string?>("--backup") { Description = "New local backup file, required with --apply." };
        command.Options.Add(user);
        command.Options.Add(minutes);
        command.Options.Add(apply);
        command.Options.Add(backup);
        command.SetAction(async (result, token) => await ExecuteAsync(result.GetValue(user), result.GetValue(minutes), result.GetValue(apply), result.GetValue(backup), token));

        return command;
    }

    private static async Task<int> ExecuteAsync(string? userId, int? minutes, bool apply, string? backup, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(userId) || minutes is <= 0 or > 1440 || (apply && string.IsNullOrWhiteSpace(backup)))
        {
            Console.Error.WriteLine("Supply a user ID, a planned duration from 1 to 1440 minutes if backfilling, and a new --backup path when applying.");

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
                Console.Error.WriteLine("Set Database:DatabaseName explicitly before repairing a workspace.");

                return 2;
            }
            var settings = Services.Services.ResolveDatabaseSettings(configuration);
            await using var scope = host.Services.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IWorkspaceRepository>();
            var document = await repository.GetAsync(userId, ct) ?? throw new InvalidOperationException("Workspace not found.");
            // Capture the original data before modifying it.
            var original = JsonSerializer.SerializeToUtf8Bytes(document);
            var workspace = WorkspaceStorageFormat.Read(document);
            var before = FocusSessionRecord.TotalSessions(workspace.Sessions);
            var missing = workspace.Sessions.Count(s => s.PlannedSeconds is null);
            var changed = minutes is { } duration ? WorkspaceStorageFormat.BackfillPlannedSeconds(workspace, duration * 60) : 0;
            Console.WriteLine($"Cosmos endpoint: {settings.AccountEndpoint}");
            Console.WriteLine($"Database: {settings.DatabaseName}; account: {userId}");
            Console.WriteLine($"Sessions: {workspace.Sessions.Count}; missing planned duration: {missing}; to backfill: {changed}");
            Console.WriteLine($"Session total across all history: {before:F2} -> {FocusSessionRecord.TotalSessions(workspace.Sessions):F2}");
            Console.WriteLine("Workspace and derived records will be written as structured Cosmos properties.");

            if (!apply)
            {
                Console.WriteLine("Preview only. Pass --apply and --backup to write.");

                return 0;
            }

            // CreateNew prevents overwriting an earlier backup.
            await using (var file = new FileStream(backup!, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.WriteAsync(original, ct);
            }
            var saved = await repository.SaveAsync(userId, workspace, document.ETag, ct);

            if (saved is null)
            {
                Console.Error.WriteLine("Workspace changed after preview; no workspace changes were applied. Re-run using a new backup file.");

                return 1;
            }
            Console.WriteLine($"Repair complete. Original workspace saved to {backup}.");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled; check the backup and workspace before retrying.");

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Repair failed ({exception.GetType().Name}). The workspace or derived records may have been partially updated; retain the backup and retry after investigating.");

            return 1;
        }
    }
}
