using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Database;
using Shared.Exceptions;

namespace CLI.Commands.User;

internal static class ResetCommand
{
    public static Command Create()
    {
        var command = new Command("reset", "Delete a user's FlowMate application data.");

        var emailOption = new Option<string>("--email")
        {
            Description = "Email address to resolve in Auth0.",
            Required = true
        };
        var userIdOption = new Option<string?>("--user-id")
        {
            Description = "Auth0 user ID, required when the email resolves to multiple accounts."
        };
        var approveOption = new Option<bool>("--approve")
        {
            Description = "Approve deletion without an interactive prompt."
        };
        emailOption.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
            {
                result.AddError("--email must not be empty.");
            }
        });
        command.Options.Add(emailOption);
        command.Options.Add(userIdOption);
        command.Options.Add(approveOption);
        command.SetAction(async (parseResult, cancellationToken) =>
            await ExecuteAsync(
                parseResult.GetValue(emailOption),
                parseResult.GetValue(userIdOption),
                parseResult.GetValue(approveOption),
                cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(
        string? email,
        string? requestedUserId,
        bool approved,
        CancellationToken cancellationToken)
    {
        try
        {
            using var host = Program.CreateHost();
            await host.StartAsync(cancellationToken);
            using var operationCancellation = Program.LinkCancellation(host, cancellationToken);
            var configuration = host.Services.GetRequiredService<IConfiguration>();
            var settings = Services.Services.ResolveDatabaseSettings(configuration);

            if (string.IsNullOrWhiteSpace(settings.AccountEndpoint) || string.IsNullOrWhiteSpace(settings.AccountKey))
            {
                throw new InvalidOperationException("Database:AccountEndpoint and Database:Key (or ConnectionStrings:database) are required for user reset.");
            }

            if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Storage")))
            {
                throw new InvalidOperationException("ConnectionStrings:Storage is required for user reset.");
            }

            var auth0 = host.Services.GetRequiredService<Auth0ManagementClient>();
            var userId = await Auth0AccountResolver.ResolveUserIdAsync(auth0, email!, requestedUserId, configuration, operationCancellation.Token);

            Console.WriteLine("FlowMate user data reset");
            Console.WriteLine($"Cosmos endpoint: {settings.AccountEndpoint}");
            Console.WriteLine($"Cosmos database: {settings.DatabaseName}");
            Console.WriteLine($"Email: {email}");
            Console.WriteLine($"Auth0 user ID: {userId}");
            Console.WriteLine("Scope: all matching workspace documents, workspace records, billing entitlements, and archived blobs under this user's exact prefix.");
            Console.WriteLine("Auth0 identities, Stripe customers/subscriptions, and shared Stripe event records are preserved.");

            if (!approved && !ResetApproval.TryApprove(Console.In, Console.Out, Console.IsInputRedirected))
            {
                Console.Error.WriteLine("Reset declined. Pass --approve to confirm in noninteractive execution.");

                return 2;
            }

            using var resetScope = host.Services.CreateScope();
            var resetService = resetScope.ServiceProvider.GetRequiredService<IUserDataResetService>();

            try
            {
                var result = await resetService.ResetAsync(userId, operationCancellation.Token);
                Console.WriteLine($"Deleted {result.WorkspaceDocuments} workspace documents, {result.WorkspaceRecords} workspace records, {result.BillingEntitlements} billing entitlements, and {result.ArchivedBlobs} archived blobs.");

                return 0;
            }
            catch (UserDataResetException exception)
            {
                var completed = exception.Completed;
                Console.Error.WriteLine($"Reset failed during {exception.Stage} cleanup. Completed before failure: {completed.WorkspaceDocuments} workspace documents, {completed.WorkspaceRecords} workspace records, {completed.BillingEntitlements} billing entitlements, and {completed.ArchivedBlobs} archived blobs. Rerun reset to finish cleanup.");

                return 1;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled. Any earlier cleanup stages may have completed; rerun reset to finish.");

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"User reset failed: {CliError.SafeError(exception)}");

            return 1;
        }
    }
}
