# FlowMate Azure deployment

The browser app and Functions API deploy separately. The browser bundle targets Azure Static Web Apps Free; the API targets a .NET isolated Functions app on Flex Consumption. This repository only builds and deploys to resources that already exist.

## Local Aspire startup

`aspire start` runs the local app, including the API, MCP Function host, and web app, and provisions the Azure-backed dependencies in `rg-flowmateai-dev`. The MCP endpoint is available with the rest of the Aspire stack. The MCP host has its own Azurite storage resource and waits for its host storage and Cosmos database before starting. The AppHost creates this dedicated group when it does not exist. Storage runs locally through Azurite.

Set the Azure subscription and region once in the AppHost's local user secrets, then start from the repository root:

```powershell
aspire secret set "Azure:SubscriptionId" "<subscription-id>" --apphost AppHost\AppHost.csproj
aspire secret set "Azure:Location" "uksouth" --apphost AppHost\AppHost.csproj
aspire start --apphost AppHost\AppHost.csproj
```

The signed-in Azure account needs permission to create resource groups and resources. Aspire's local Azure resources are billable. The separate GitHub Actions workflow below continues to deploy the Static Web App and Functions API to resources that already exist.

## GitHub Actions settings

For the Auth0 dashboard setup and the exact frontend/API values, see [Auth0 setup for Azure](auth0-azure-setup.md).

Configure these repository secrets:

- `AZURE_STATIC_WEB_APPS_API_TOKEN`: deployment token for the Static Web App.
- `AZURE_FUNCTIONAPP_PUBLISH_PROFILE`: publish profile for the Flex Consumption Function App.
- `AZURE_MCP_FUNCTIONAPP_PUBLISH_PROFILE`: publish profile for the separate MCP Function App.

Configure this repository variable:

- `AZURE_FUNCTIONAPP_NAME`: existing Function App name.
- `AZURE_MCP_FUNCTIONAPP_NAME`: separate Linux Flex Consumption Function App for MCP.
- `MCP_ENDPOINT`: MCP endpoint URL including `/runtime/webhooks/mcp` for the Settings instructions.
- `API_BASE_URL`: HTTPS origin of the Functions API.
- `AUTH0_DOMAIN`, `AUTH0_CLIENT_ID`, `AUTH0_AUDIENCE`: browser-safe Auth0 SPA configuration. These values are public client settings, not secrets.

## Function App settings

Set `Auth0__Authority` to the Auth0 tenant issuer including `https://` and the trailing slash, and `Auth0__Audience` to the API audience registered in Auth0. Set `Cors__AllowedOrigins__0` to the exact Static Web App origin, with no path or trailing slash. Also configure `Database__AccountEndpoint`, `Database__Key`, `Database__DatabaseName`, and `ConnectionStrings__Storage` from server-side settings or a secret store. The current Cosmos provider uses the account key, so supply it through a protected Function App setting or Key Vault reference.

Set `Stripe__SecretKey`, `Stripe__WebhookSecret`, `Stripe__MonthlyPriceId`, `Stripe__AnnualPriceId`, and `WebApp__Origin` on the Function App. Register `https://<function-app-host>/api/webhooks/stripe` in Stripe and subscribe to `customer.subscription.created`, `customer.subscription.updated`, and `customer.subscription.deleted`. Configure `Foundry__Endpoint`, `Foundry__ApiKey`, `Foundry__Deployment`, and `Foundry__ApiVersion` on the Function App; the coach sends the user's own tasks, projects, and focus totals to that deployment.

Set the web app's `ApiBaseUrl` to the HTTPS Functions API origin. Configure the Auth0 SPA client with the Static Web App origin and callback/logout URLs. Register separate Google, GitHub, Microsoft personal, and Microsoft work/school connections in Auth0. Keep Stripe, Foundry, Cosmos, and storage credentials in server-side app settings or Key Vault; do not add them to the Blazor app settings. `API/appsettings.example.json` lists the expected server-side keys.

Cosmos stores the account workspace, billing entitlements, webhook idempotency keys, and a `WorkspaceRecords` container with user-partitioned, individually queryable projects, tasks, focus sessions, conversations, and timer state. Completed focus records are also written as immutable JSON blobs under the private `flowmate-activity` container; reports read the report-facing Cosmos/workspace records and do not scan the blob archive. Before deploying MCP, run `infra/provision-mcp-containers.sh` to provision `McpKeys` and TTL-enabled `McpUsage`. See [MCP server setup](mcp-server.md) for Cosmos role scopes. The MCP Function App uses managed identity with read access to workspace, entitlement, and key containers, plus contributor access only to the usage container.

The included workflow runs on `main` and on manual dispatch. It does not create Azure resources. Static Web Apps' Free plan does not provide a linked Functions `/api` backend, so the API remains a separately hosted app and browser CORS must list the exact site origin.
