using System.CommandLine;

namespace CLI.Commands.User;

internal static class UserCommand
{
    public static Command Create()
    {
        var command = new Command("user", "User administration");
        command.Subcommands.Add(GrantProCommand.Create());
        command.Subcommands.Add(ResetCommand.Create());

        return command;
    }
}
