using System.CommandLine;

namespace CLI.Commands.Migrations;

internal static class MigrationsCommand
{
    public static Command Create()
    {
        var command = new Command("migrations", "Data migrations");
        command.Subcommands.Add(ProjectCutoversCommand.Create());

        return command;
    }
}
