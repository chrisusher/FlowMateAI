# FlowMate administrative CLI

The `CLI` project provides explicit administrative operations. Running the AppHost does not run either operation; the `cli` resource starts only when requested and displays help by default.

## Configuration

The CLI loads `appsettings.json`, `appsettings.{Environment}.json`, CLI user secrets, and then environment variables. Environment variables take precedence. Copy `CLI/appsettings.example.json` to `CLI/appsettings.json` for local values, or use environment variables/user secrets for credentials. `CLI/appsettings.json` is not tracked.

Cosmos settings use the same resolution as the API: `Database:AccountEndpoint` (or `Database:AccountName`) and `Database:Key`, with `ConnectionStrings:database` accepted as a fallback source for endpoint and key. `Database:DatabaseName` is optional; without it the CLI uses `flowmate-{Global:Environment}`. Blob cleanup requires `ConnectionStrings:Storage`.

For user reset, configure:

```text
Auth0:Authority                 https://YOUR_TENANT.eu.auth0.com/
Auth0:ManagementClientId        (secret)
Auth0:ManagementClientSecret    (secret)
```

Create an Auth0 machine-to-machine application authorized for the Auth0 Management API with the `read:users` scope. The CLI requests a Management API token and resolves email through `/api/v2/users-by-email`. See [Auth0's production token setup](https://auth0.com/docs/secure/tokens/access-tokens/management-api-access-tokens/get-management-api-access-tokens-for-production).

## Grant Pro access for a preview

`user grant-pro` resolves an existing Auth0 account by email and upserts its Cosmos billing entitlement to `Pro` with an active status. It grants the same Pro feature limits used by the app, without creating a Stripe customer or subscription. It does not create an Auth0 account; the email must already belong to one. If multiple Auth0 accounts match, pass `--user-id` to select one. The command leaves the account's Stripe identifiers, trial history, and usage counters intact.

## Standalone usage

From the repository root:

```powershell
dotnet run --project CLI -- --help
dotnet run --project CLI -- database ensure-created
dotnet run --project CLI -- user grant-pro --email person@example.com
dotnet run --project CLI -- user grant-pro --email person@example.com --user-id "auth0|abc123"
dotnet run --project CLI -- user reset --email person@example.com
dotnet run --project CLI -- user reset --email person@example.com --user-id "auth0|abc123" --approve
```

`database ensure-created` reports whether EF Core created Cosmos resources or found them already present. It does not require Storage or Auth0 settings.

`user reset` shows the Cosmos endpoint/database, email, Auth0 user ID, and deletion scope before prompting. The prompt defaults to No. Redirected/noninteractive runs require `--approve`. If an email resolves to multiple Auth0 accounts, the CLI prints their IDs and requires `--user-id`; the supplied ID must belong to the lookup results.

Reset deletes all workspace documents, workspace records, billing entitlements, and archived blobs under the exact escaped user prefix in `flowmate-activity`. Removing billing entitlements resets the application plan, trial history, and coach usage. It preserves Auth0 identities, external Stripe customers/subscriptions, and shared Stripe event deduplication records. A later Stripe webhook can recreate billing entitlements.

Cosmos and Blob Storage cleanup cannot be one transaction. If a stage fails, the CLI reports the failed stage, the completed deletion counts, and that cleanup may be partial. Rerun reset to finish remaining work. Users should close their existing browser sessions before reset and reload after it completes.

## Aspire dashboard and resource commands

In the dashboard, use the `cli` resource's **Ensure database created** command for the database operation. Use **Reset user data** to provide a required email, optional Auth0 user ID, and set **Approve deletion** to true before launch. With approval left false, the reset process is not started.

Use **Create new Pro account** on the `cli` resource to grant Pro preview access. Enter the email address of an existing Auth0 account; an Auth0 user ID can optionally disambiguate duplicate email matches. The command requires Cosmos and Auth0 Management API configuration and does not create a Stripe subscription.

Equivalent Aspire CLI commands (from the AppHost directory) are:

```powershell
aspire resource cli ensure-database-created
aspire resource cli create-pro-account --email person@example.com
aspire resource cli create-pro-account --email person@example.com --user-id "auth0|abc123"
aspire resource cli reset-user-data --email person@example.com --approve true
aspire resource cli reset-user-data --email person@example.com --user-id "auth0|abc123" --approve true
```

The reset process is launched as `dotnet <absolute CLI.dll path> ...` with an absolute working directory. Its stdout/stderr and exit status are forwarded by Aspire. The resource is excluded from the publish manifest; this is a local administration tool.
