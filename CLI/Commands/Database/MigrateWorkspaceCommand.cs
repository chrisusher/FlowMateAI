using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Services.Database;
using Services.Repositories;

namespace CLI.Commands.Database;

internal static class MigrateWorkspaceCommand
{
    public static Command Create()
    {
        var command = new Command("migrate-workspace", "Copy one legacy workspace's collections into their new containers. Stop all workspace writers first.");
        var userId = new Option<string>("--user-id") { Description = "Exact workspace owner ID.", Required = true };
        var apply = new Option<bool>("--apply") { Description = "Copy records after preview; stop all workspace writers first." };
        userId.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>())) result.AddError("--user-id must not be empty.");
        });
        command.Options.Add(userId);
        command.Options.Add(apply);
        command.SetAction(async (result, token) => await ExecuteAsync(result.GetValue(userId)!, result.GetValue(apply), token));

        return command;
    }

    private static async Task<int> ExecuteAsync(string userId, bool apply, CancellationToken cancellationToken)
    {
        try
        {
            using var host = Program.CreateHost();
            await host.StartAsync(cancellationToken);
            using var operationCancellation = Program.LinkCancellation(host, cancellationToken);
            await using var scope = host.Services.CreateAsyncScope();
            var repository = new WorkspaceMigrationRepository(scope.ServiceProvider.GetRequiredService<DatabaseContext>());
            var result = await repository.MigrateAsync(userId, apply, operationCancellation.Token);
            Console.WriteLine($"{(apply ? "Verified" : "Preview:")} {result.Projects} projects, {result.Tasks} tasks, {result.Sessions} sessions and {result.Conversations} conversations. The source workspace was retained.");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled. Rerun after stopping all writers.");

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Workspace migration failed: {CliError.SafeError(exception)}");

            return 1;
        }
    }
}
