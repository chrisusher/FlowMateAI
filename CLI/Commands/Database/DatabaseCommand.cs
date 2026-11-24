using System.CommandLine;

namespace CLI.Commands.Database;

internal static class DatabaseCommand
{
    public static Command Create()
    {
        var command = new Command("database", "Database administration");
        command.Subcommands.Add(EnsureCreatedCommand.Create());
        command.Subcommands.Add(MigrateCosmosContainersCommand.Create());

        return command;
    }
}
