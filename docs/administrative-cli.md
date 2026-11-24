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

Create an Auth0 machine-to-machine application authorized for the Auth0 Management API with the `read:users` scope. The authenticated account-deletion endpoint additionally requires `delete:users`; the CLI itself only resolves email through `/api/v2/users-by-email`. The CLI requests a Management API token through the same server-side settings. See [Auth0's production token setup](https://auth0.com/docs/secure/tokens/access-tokens/management-api-access-tokens/get-management-api-access-tokens-for-production).

## Grant Pro access for a preview

`user grant-pro` resolves an existing Auth0 account by email and upserts its Cosmos billing entitlement to `Pro` with an active status and monthly interval. It sets a simulated period end one month from the grant date so the billing screen can display monthly renewal details. This is preview data only: it does not create a Stripe subscription, renewal, or payment, and Pro access does not automatically expire at that date. It does not create an Auth0 account; the email must already belong to one. If multiple Auth0 accounts match, pass `--user-id` to select one. The command leaves the account's Stripe identifiers, trial history, and usage counters intact.

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

## Repair a workspace and historical focus totals

`user repair-workspace` targets one exact Cosmos `/UserId` partition. It previews by default. It reads both legacy JSON-string workspaces and structured workspaces, then writes the workspace and its derived records as native Cosmos properties. It never changes the historical blob archive. It leaves existing planned durations untouched. Specify `--planned-minutes` only when you have confirmed the missing records used that duration; the original duration cannot be recovered reliably from old records. The standard focus timer is 25 minutes.

Set `Database:DatabaseName` and the endpoint/key explicitly for the intended environment, then close the account's active browser tabs and resolve any pending local changes. Check the printed endpoint, database, user ID, record count and all-history before/after totals before applying. For example:

```powershell
dotnet run --project CLI -- user repair-workspace --user-id "auth0|abc123" --planned-minutes 25
dotnet run --project CLI -- user repair-workspace --user-id "auth0|abc123" --planned-minutes 25 --apply --backup ./test-workspace-backup.json
```

Omit `--planned-minutes` to migrate the storage representation without changing historical counts. Applying requires a new backup file; the command refuses to overwrite an existing file and uses the original Cosmos ETag to reject concurrent workspace changes. Keep the backup private because it contains the complete workspace. The workspace and derived records are separate writes, so failures can leave the latter partially updated; after checking the workspace and backup, rerun the command to regenerate them. Back up the Cosmos account independently before bulk administrative work. Reopen the app and check the relevant reporting period after repair. Unsynchronised browser drafts can conflict and must not be blindly used to overwrite the repaired workspace.

## Cut over an account's projects

New empty workspaces initialise the project container automatically. Before moving an existing account, resolve its pending browser drafts, back up the Cosmos account independently, and stop **all** old and new API instances, background workspace writers and browser writers for the entire copy and verification. A rolling deployment with old writers running is unsafe: an in-flight legacy projection synchronisation can overwrite project documents after cutover. Deploy the project-aware API, web client and MCP reader, provision the `Projects` container and MCP read permission, and require updated clients before resuming writes.

Configure the intended Cosmos endpoint and `Database:DatabaseName` explicitly. Preview the exact account, check the printed endpoint, database, account and counts, then opt in to the write with a new backup path and confirmation that writers are stopped:

```powershell
dotnet run --project CLI -- migrations project-cutovers --user-id "auth0|abc123"
dotnet run --project CLI -- migrations project-cutovers --user-id "auth0|abc123" --apply --writers-stopped --backup ./project-cutover-backup.json
```

The command refuses to overwrite different destination records or an existing backup, preserves the source workspace, and checks its ETag again before switching authority. The backup contains the full workspace and account identifier; store it privately and retain it separately. Partial copies can be retried with a new backup only if the source and destination still match. After applying, verify project order, names, archives, tasks, timer behaviour, reports and MCP access before resuming writers.

For rollback, stop writers again and preserve the **current** workspace and project containers before any restore. The retained pre-cutover backup predates subsequent edits and must not be blindly written over them. Restore into an isolated database from the independent Cosmos backup, compare and reconcile all newer project and workspace changes, then use a separately reviewed recovery procedure before directing traffic to the restored data. Do not resume old clients against a cut-over account.
