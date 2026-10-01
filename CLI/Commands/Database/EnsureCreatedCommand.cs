using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Services.Database;

namespace CLI.Commands.Database;

internal static class EnsureCreatedCommand
{
    public static Command Create()
    {
        var command = new Command("ensure-created", "Create the configured Cosmos database resources if missing.");
        command.SetAction(ExecuteAsync);

        return command;
    }

    private static async Task<int> ExecuteAsync(ParseResult _, CancellationToken cancellationToken)
    {
        try
        {
            using var host = Program.CreateHost();
            await host.StartAsync(cancellationToken);

            using var operationCancellation = Program.LinkCancellation(host, cancellationToken);
            await using var scope = host.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var created = await context.Database.EnsureCreatedAsync(operationCancellation.Token);

            Console.WriteLine(created
                ? "Database resources were created."
                : "Database resources already existed.");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Database creation failed: {CliError.SafeError(exception)}");

            return 1;
        }
    }
}
