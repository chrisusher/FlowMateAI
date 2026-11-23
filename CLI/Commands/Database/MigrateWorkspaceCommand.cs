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
        userId.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>())) result.AddError("--user-id must not be empty.");
        });
        command.Options.Add(userId);
        command.SetAction(async (result, token) => await ExecuteAsync(result.GetValue(userId)!, token));

        return command;
    }

    private static async Task<int> ExecuteAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var host = Program.CreateHost();
            await host.StartAsync(cancellationToken);
            using var operationCancellation = Program.LinkCancellation(host, cancellationToken);
            await using var scope = host.Services.CreateAsyncScope();
            var repository = new WorkspaceMigrationRepository(scope.ServiceProvider.GetRequiredService<DatabaseContext>());
            var result = await repository.MigrateAsync(userId, operationCancellation.Token);
            Console.WriteLine($"Verified {result.Projects} projects, {result.Tasks} tasks, {result.Sessions} sessions and {result.Conversations} conversations. The source workspace was retained.");

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
