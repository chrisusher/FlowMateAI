# Test environment

The resource-group-scoped template uses an existing Cosmos DB for NoSQL account and creates the specified database and the application's containers. Choose a separate database for Test if data isolation is required. The Cosmos account resource group defaults to `Shared-Resources`; set `cosmosResourceGroup` if it is elsewhere in the same subscription. The deploying principal needs permission to create databases and containers there.

Deploy to a new resource group (replace the example values, and select a region with Flex Consumption and GPT-6 Luna and Azure OpenAI GlobalStandard quota):

```sh
az group create --name <new-resource-group> --location <region>
az deployment group what-if --resource-group <new-resource-group> --template-file infra/environments/test/main.bicep --parameters cosmosEndpoint=<existing-cosmos-endpoint> cosmosResourceGroup=<cosmos-resource-group> cosmosDatabaseName=<database-name> auth0Authority=<authority-url> auth0Audience=<audience>
az deployment group create --resource-group <new-resource-group> --template-file infra/environments/test/main.bicep --parameters cosmosEndpoint=<existing-cosmos-endpoint> cosmosResourceGroup=<cosmos-resource-group> cosmosDatabaseName=<database-name> auth0Authority=<authority-url> auth0Audience=<audience>
```

The resource name prefix defaults to `flowmate`; set `prefix` to override it. Use `staticWebAppLocation` if the default `westeurope` is unsuitable. The deploying principal needs permission to create resources and role assignments. Azure OpenAI model availability, access and quota must be in place. Resource names include a deterministic resource-group suffix; do not change the prefix after deployment.

The partition key is `/UserId` for `BillingEntitlements`, `McpKeys`, `Users`, `WorkspaceDocuments`, `WorkspaceRecords`, `Projects`, `Tasks`, `Sessions` and `Conversations`, `/PartitionId` for `StripeEvents`, and `/userId` for `McpUsage`. New empty workspaces use project documents as the authority; existing accounts require the guarded project cutover described in [the CLI guide](../docs/administrative-cli.md). A per-user index in `Projects` reserves active slots transactionally with project records. Tasks become authoritative in their own container after the guarded task cutover; sessions and conversations remain authoritative in the workspace and are synchronised to collection containers. `McpUsage` has a default TTL of 172800 seconds. Partition keys cannot be changed in place: review what-if and use a new database or migrate data if an existing container uses a different key. The template does not create the Cosmos account or populate its key in Key Vault.

To prepare one existing workspace for the future cutover, first provision the four collection containers and stop all workspace writers for that user throughout migration and verification. Configure the CLI for the target Cosmos database, then run:

```sh
dotnet run --project CLI -- database migrate-cosmos-containers --user-id '<workspace-owner-id>'
dotnet run --project CLI -- database migrate-cosmos-containers --user-id '<workspace-owner-id>' --apply
```

The first command previews the migration; the second copies and verifies the four collections, preserves their order and nested data, and leaves the source workspace untouched. It can resume after interruption when existing destination records match; it refuses to overwrite differing or unexpected destination records. A successful run does not switch authority. Use the separate backed-up project cutover only after preparing the deployment and stopping all writers. Workspace saves continue synchronising tasks, sessions and conversations; these separate writes are not atomic.

Populate the new vault with `cosmos-key`, `auth0-management-client-id`, `auth0-management-client-secret`, `stripe-secret-key`, `stripe-webhook-secret`, `stripe-monthly-price-id`, `stripe-annual-price-id`, and `foundry-api-key`. The last is a key from the newly created Azure OpenAI account. Use Test credentials and the existing Cosmos account key. A principal with Key Vault Secrets Officer access can set these using `az keyvault secret set`; do not put values in source control or command history. The API identity receives Key Vault Secrets Officer access to create and delete MCP credentials; the MCP identity receives Key Vault Secrets User access. Key Vault reference resolution can take time after secrets or role assignments change; restart the apps or refresh references when provisioning is complete.

Configure the GitHub `Test` environment before running the deployment workflow. Set variables `AZURE_FUNCTIONAPP_NAME`, `AZURE_MCP_FUNCTIONAPP_NAME`, `API_BASE_URL`, `MCP_ENDPOINT`, `APP_URL`, `SUPPORT_URL`, `AUTH0_DOMAIN`, `AUTH0_CLIENT_ID` and `AUTH0_AUDIENCE`. Use the deployment outputs for app names and URLs; `APP_URL` is the application SWA URL. Configure the corresponding Auth0 callback, logout and allowed origins, and Stripe webhook endpoint.

Set environment secrets `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` and `AZURE_MCP_FUNCTIONAPP_PUBLISH_PROFILE` from each new Function App, plus `AZURE_STATIC_WEB_APPS_API_TOKEN` and `AZURE_MARKETING_STATIC_WEB_APPS_API_TOKEN` from the respective Static Web Apps. Publish profiles require SCM basic authentication; follow your organisation's credential policy. The workflow deploys all four applications on a push to `main` or manual dispatch. The workflow publishes browser configuration into public files, so those variables must not contain secrets.

The template configures both the API Function App's platform CORS allowlist and its application CORS setting for the generated application hostname. If using custom domains, update both allowlists and Auth0 configuration. The platform CORS allowlist is managed by the template, so include every required origin in it. The application deployment workflow does not update infrastructure; deploy the template to apply infrastructure changes. After deployment, verify the API Function App's CORS settings with `az functionapp cors show --resource-group <resource-group> --name <api-function-app-name>`. The existing `mcp-functionapp.bicep` is a legacy standalone template, not used by this environment.
