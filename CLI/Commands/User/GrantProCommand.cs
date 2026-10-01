using System.CommandLine;
using CLI.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Services.Database;
using Services.Repositories;

namespace CLI.Commands.User;

internal static class GrantProCommand
{
    public static Command Create()
    {
        var command = new Command("grant-pro", "Grant an existing FlowMate account Pro access for preview.");
        var emailOption = new Option<string>("--email") { Description = "Email address of the existing Auth0 account.", Required = true };
        var userIdOption = new Option<string?>("--user-id") { Description = "Auth0 user ID, required when the email resolves to multiple accounts." };
        emailOption.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
            {
                result.AddError("--email must not be empty.");
            }
        });
        command.Options.Add(emailOption);
        command.Options.Add(userIdOption);
        command.SetAction(async (parseResult, cancellationToken) =>
            await ExecuteAsync(parseResult.GetValue(emailOption), parseResult.GetValue(userIdOption), cancellationToken));

        return command;
    }

    private static async Task<int> ExecuteAsync(string? email, string? requestedUserId, CancellationToken cancellationToken)
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
                throw new InvalidOperationException("Database:AccountEndpoint and Database:Key (or ConnectionStrings:database) are required for granting Pro access.");
            }

            var auth0 = host.Services.GetRequiredService<Auth0ManagementClient>();
            var userId = await Auth0AccountResolver.ResolveUserIdAsync(auth0, email!, requestedUserId, configuration, operationCancellation.Token);

            await using var scope = host.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var repository = new BillingRepository(database);
            var entitlement = await repository.GetEntitlementAsync(userId, operationCancellation.Token);
            entitlement.Plan = "Pro";
            entitlement.SubscriptionStatus = "active";
            await repository.SaveEntitlementAsync(entitlement, operationCancellation.Token);

            Console.WriteLine("Pro access granted.");
            Console.WriteLine($"Email: {email}");
            Console.WriteLine($"Auth0 user ID: {userId}");
            Console.WriteLine($"Cosmos database: {settings.DatabaseName}");
            Console.WriteLine("This is a manual preview grant; no Stripe subscription or payment was created.");

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");

            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pro access grant failed: {CliError.SafeError(exception)}");

            return 1;
        }
    }
}
