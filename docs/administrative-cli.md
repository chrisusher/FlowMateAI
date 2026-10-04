# FlowMate administrative CLI

The `CLI` project provides explicit administrative operations. Running the AppHost does not run either operation; the `cli` resource starts only when requested and displays help by default.

## Configuration

The CLI loads `appsettings.json`, `appsettings.{Environment}.json`, CLI user secrets, and then environment variables. Environment variables take precedence. Copy `CLI/appsettings.example.json` to `CLI/appsettings.json` for local values, or use environment variables/user secrets for credentials. `CLI/appsettings.json` is not tracked.

Cosmos settings use the same resolution as the API: `Database:AccountEndpoint` (or `Database:AccountName`) and `Database:Key`, with `ConnectionStrings:database` accepted as a fallback source for endpoint and key. `Database:DatabaseName` is optional; without it the CLI uses `flowmate-{Global:Environment}`. Blob cleanup requires `ConnectionStrings:Storage`.

For user reset, configure the Keycloak administrative service account:

```text
Keycloak:Url                    https://YOUR_KEYCLOAK_HOST/
Keycloak:Realm                  flowmate
Keycloak:ManagementClientId     flowmate-cli
Keycloak:ManagementClientSecret (secret)
```

Configure the `flowmate-cli` Keycloak client for client credentials and grant its service account only `query-users` and `view-users` from `realm-management`. The CLI obtains a token from the configured realm and paginates Admin REST email queries, filtering exact addresses before changing account data.

## Grant Pro access for a preview

`user grant-pro` resolves an existing Keycloak account by email and upserts its Cosmos billing entitlement to `Pro` with an active status. It grants the same Pro feature limits used by the app, without creating a Stripe customer or subscription. It does not create a Keycloak account; the email must already belong to one. If multiple accounts match, pass `--user-id` to select one. The command leaves the account's Stripe identifiers, trial history, and usage counters intact.

## Standalone usage

From the repository root:

```powershell
dotnet run --project CLI -- --help
dotnet run --project CLI -- database ensure-created
dotnet run --project CLI -- user grant-pro --email person@example.com
dotnet run --project CLI -- user grant-pro --email person@example.com --user-id "9d35d31b-1bf4-4a86-8a7a-19f948424010"
dotnet run --project CLI -- user reset --email person@example.com
dotnet run --project CLI -- user reset --email person@example.com --user-id "9d35d31b-1bf4-4a86-8a7a-19f948424010" --approve
```

`database ensure-created` reports whether EF Core created Cosmos resources or found them already present. It does not require Storage or Keycloak settings.

`user reset` shows the Cosmos endpoint/database, email, Keycloak user ID, and deletion scope before prompting. The prompt defaults to No. Redirected/noninteractive runs require `--approve`. If an email resolves to multiple accounts, the CLI prints their IDs and requires `--user-id`; the supplied ID must belong to the lookup results.

Reset deletes all workspace documents, workspace records, billing entitlements, and archived blobs under the exact escaped user prefix in `flowmate-activity`. Removing billing entitlements resets the application plan, trial history, and coach usage. It preserves Keycloak identities, external Stripe customers/subscriptions, and shared Stripe event deduplication records. A later Stripe webhook can recreate billing entitlements.

Cosmos and Blob Storage cleanup cannot be one transaction. If a stage fails, the CLI reports the failed stage, the completed deletion counts, and that cleanup may be partial. Rerun reset to finish remaining work. Users should close their existing browser sessions before reset and reload after it completes.

## Aspire dashboard and resource commands

In the dashboard, use the `cli` resource's **Ensure database created** command for the database operation. Use **Reset user data** to provide a required email, optional Keycloak user ID, and set **Approve deletion** to true before launch. With approval left false, the reset process is not started.

Use **Create new Pro account** on the `cli` resource to grant Pro preview access. Enter the email address of an existing Keycloak account; a Keycloak user ID can optionally disambiguate duplicate email matches. The command requires Cosmos and Keycloak service-account configuration and does not create a Stripe subscription.

Equivalent Aspire CLI commands (from the AppHost directory) are:

```powershell
aspire resource cli ensure-database-created
aspire resource cli create-pro-account --email person@example.com
aspire resource cli create-pro-account --email person@example.com --user-id "9d35d31b-1bf4-4a86-8a7a-19f948424010"
aspire resource cli reset-user-data --email person@example.com --approve true
aspire resource cli reset-user-data --email person@example.com --user-id "9d35d31b-1bf4-4a86-8a7a-19f948424010" --approve true
```

The reset process is launched as `dotnet <absolute CLI.dll path> ...` with an absolute working directory. Its stdout/stderr and exit status are forwarded by Aspire. The resource is excluded from the publish manifest; this is a local administration tool.
