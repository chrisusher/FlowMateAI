# Keycloak authentication and operations

FlowMate uses a separate Keycloak realm and PostgreSQL database for each environment. The browser uses the public `flowmate-web` client with authorization code and PKCE S256. The API accepts RS256 access tokens from the exact `Authentication:Authority` realm issuer and requires audience `flowmate-api`. The browser adapter is vendored at `Web/wwwroot/js/vendor/keycloak.js` from the pinned `keycloak-js` 26.2.4 npm package; its Apache 2.0 license is alongside the file.

## Local development

The Aspire AppHost starts Keycloak with a persistent PostgreSQL data volume and imports the baseline realm from `infra/keycloak/realm-import/flowmate-realm.json`. Aspire exposes a stable Keycloak port and gives the API its server-side issuer. The gateway injects browser-safe endpoint and client settings. The imported realm defaults enable email registration, verification and recovery, brute-force protection, the 15-character password policy, PKCE, and the API audience mapper.

Run `infra/keycloak/provision_realm.py` after starting Keycloak to set exact browser callback/origin allow-lists and any configured provider or SMTP credentials. It uses only Python's standard library. Re-running it updates realm settings and clients in place; it does not import over or delete users, credentials, or realm signing keys.

Set these variables in the shell used to run the provisioning script:

```text
KEYCLOAK_URL=http://localhost:8080
KEYCLOAK_REALM=flowmate
KEYCLOAK_ADMIN_USERNAME=admin
KEYCLOAK_ADMIN_PASSWORD=<local Aspire admin password>
FLOWMATE_FRONTEND_REDIRECT_URIS=http://localhost:<gateway-port>/frontend/
FLOWMATE_FRONTEND_ORIGINS=http://localhost:<gateway-port>
```

Provider client credentials are optional at provisioning time. Configure them as secrets for each environment:

```text
IDP_GOOGLE_CLIENT_ID / IDP_GOOGLE_CLIENT_SECRET
IDP_GITHUB_CLIENT_ID / IDP_GITHUB_CLIENT_SECRET
IDP_MICROSOFT_PERSONAL_CLIENT_ID / IDP_MICROSOFT_PERSONAL_CLIENT_SECRET
IDP_MICROSOFT_WORK_SCHOOL_CLIENT_ID / IDP_MICROSOFT_WORK_SCHOOL_CLIENT_SECRET
SMTP_HOST / SMTP_PORT / SMTP_FROM / SMTP_USER / SMTP_PASSWORD
SMTP_AUTH / SMTP_SSL / SMTP_STARTTLS
```

Create upstream provider applications with Keycloak's broker callback `https://<keycloak-host>/realms/flowmate/broker/<alias>/endpoint`. Keep provider credentials in Key Vault or local user secrets. Set `trustEmail` false so matching email addresses alone do not link identities. A user must authenticate with both providers through Keycloak's account-link flow.

## Environment configuration

The WebAssembly app reads `Keycloak:Url`, `Keycloak:Realm`, and `Keycloak:ClientId`; provider aliases are under `Keycloak:IdentityProviders`. The API reads `Authentication:Authority` and `Authentication:Audience`. The CLI reads `Keycloak:Url`, `Keycloak:Realm`, `Keycloak:ManagementClientId`, and `Keycloak:ManagementClientSecret`. Set the CLI service-account client to use client credentials and grant only `query-users` and `view-users` from `realm-management`.

`FLOWMATE_FRONTEND_REDIRECT_URIS` and `FLOWMATE_FRONTEND_ORIGINS` are explicit comma-separated allow-lists. Set the exact root callback for Static Web Apps and the exact `/frontend/` callback for local Aspire. Avoid wildcard redirects. Public browser settings may be embedded in the static bundle; never put a client secret there.

## Azure Container Apps

Use `infra/keycloak/main.bicep` and the Test or Production parameter files. The modules deploy one always-on Keycloak replica with HTTPS ingress on 8080, health checks on private management port 9000, a digest-pinned image, managed identity for ACR and Key Vault, and a private Azure PostgreSQL Flexible Server with TLS and 14-day backups. The `infra/keycloak/Dockerfile` builds an optimized Keycloak image and includes the versioned realm baseline. Use the Azure Container Apps hostname until a custom domain and certificate are configured.

Configure separate GitHub `Test` and `Production` environments. Each needs `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `AZURE_LOCATION`, `KEYCLOAK_ACR_NAME`, `KEYCLOAK_POSTGRES_SERVER_NAME`, `KEYCLOAK_KEY_VAULT_NAME`, `FLOWMATE_FRONTEND_ORIGINS`, and `FLOWMATE_FRONTEND_REDIRECT_URIS`. The federated deployment identity needs permission to create the listed infrastructure, push/import ACR images, and set Key Vault secrets. Set environment secrets `KEYCLOAK_POSTGRES_ADMIN_PASSWORD`, `KEYCLOAK_BOOTSTRAP_ADMIN_PASSWORD`, `KEYCLOAK_CLI_CLIENT_SECRET`, all four `IDP_*_CLIENT_ID`/`IDP_*_CLIENT_SECRET` pairs, and the `SMTP_*` values. The workflow writes provider, mail, and CLI client credentials into that environment's Key Vault, then reconciles them into the realm. Create separate Key Vaults, databases, realms, providers, and signing keys for Test and Production. After initial setup, remove the temporary bootstrap administrator and use MFA-protected administrator accounts.

The Keycloak workflow deploys infrastructure and a digest-pinned Keycloak revision, checks OIDC discovery, and reconciles the realm without replacing existing accounts or signing keys. The application workflow builds against the selected environment's discovery document; pushes to `main` deploy to `Test`, while `Production` requires a manual workflow dispatch. Set `KEYCLOAK_URL`, `KEYCLOAK_REALM`, `KEYCLOAK_CLIENT_ID`, `API_BASE_URL`, `MCP_ENDPOINT`, `FLOWMATE_FRONTEND_ORIGIN`, `AZURE_FUNCTIONAPP_NAME`, and `AZURE_MCP_FUNCTIONAPP_NAME` as environment variables, plus the three Static Web Apps/Functions publish credentials as environment secrets. It writes the exact realm issuer, API audience, and browser origin to Function App settings during release. Promote the same image digest from Test to Production only after the browser/API sign-in gate passes. Production additionally needs `TEST_KEYCLOAK_ACR_NAME` to import the verified Test image and confirm its digest.

## Recovery and upgrades

Before upgrades, take and retain a PostgreSQL backup and record the deployed image digest. Upgrade Keycloak against a restored Test backup first, reconcile the realm, and exercise sign-in, account linking, email verification, recovery, and CLI lookups. Keep database schema changes compatible with the previous image before attempting rollback. Do not restore a Test backup into Production.

For recovery, restore the PostgreSQL backup into a separate server, point a stopped Keycloak revision at it, validate realm discovery and login, then cut over ACA traffic. The identity database contains accounts and credentials; protecting and restoring it is required to retain existing users. The local Aspire volume is disposable development data and is independent of Azure environments.

## Release gates

Before production cutover, verify the provider callbacks and email actions using the real environment's provider registrations and SMTP. Validate an access token, rotated signing key, wrong issuer/audience, ID token, expired token, and missing subject. Exercise concurrent token refresh, cancellation/deep-link return, logout, a browser with third-party cookies blocked, and cross-user workspace/billing/MCP isolation. Keep the old Auth0 resources and application-owned records intact until the release gates pass; this migration intentionally does not transfer ownership or delete prior records.
