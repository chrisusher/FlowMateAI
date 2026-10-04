using System.CommandLine;
using CLI.Commands.Database;
using CLI.Commands.User;
using Shared.Exceptions;

namespace CLI.Commands;

internal static class CliCommandLine
{
    public static RootCommand Create()
    {
        var root = new RootCommand("FlowMate administrative CLI");
        root.Subcommands.Add(DatabaseCommand.Create());
        root.Subcommands.Add(UserCommand.Create());

        return root;
    }
}

internal static class CliError
{
    public static string SafeError(Exception exception) => exception switch
    {
        KeycloakManagementException keycloak => keycloak.Message,
        InvalidOperationException invalid when invalid.Message is "ConnectionStrings:Storage is required for user reset."
            or "Database:AccountEndpoint and Database:Key (or ConnectionStrings:database) are required for user reset."
            or "Database:AccountEndpoint and Database:Key (or ConnectionStrings:database) are required for granting Pro access." => invalid.Message,
        _ => "an operation error occurred. Check configuration and service availability."
    };
}
