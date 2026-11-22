# Test environment

The resource-group-scoped template creates new resources independently of the existing `-TEST` resource group. It uses the existing Cosmos account and database; it does not create or change them. Choose a separate Cosmos database for Test if data isolation is required, and provision its containers before starting the applications. The scripts in this directory describe MCP container prerequisites.

Deploy to a new resource group (replace the example values, and select a region with Flex Consumption and the chosen Azure OpenAI GlobalStandard model and quota):

```sh
az group create --name <new-resource-group> --location <region>
az deployment group what-if --resource-group <new-resource-group> --template-file infra/environments/test/main.bicep --parameters prefix=<lowercase-alphanumeric-prefix> cosmosEndpoint=<existing-cosmos-endpoint> cosmosDatabaseName=<existing-database> auth0Authority=<authority-url> auth0Audience=<audience> foundryModelName=<model-name> foundryModelVersion=<model-version>
az deployment group create --resource-group <new-resource-group> --template-file infra/environments/test/main.bicep --parameters prefix=<lowercase-alphanumeric-prefix> cosmosEndpoint=<existing-cosmos-endpoint> cosmosDatabaseName=<existing-database> auth0Authority=<authority-url> auth0Audience=<audience> foundryModelName=<model-name> foundryModelVersion=<model-version>
```

Use `staticWebAppLocation` if the default `westeurope` is unsuitable. The deploying principal needs permission to create resources and role assignments. Azure OpenAI model availability, access and quota must be in place. Resource names include a deterministic resource-group suffix; do not change the prefix after deployment.

Populate the new vault with `cosmos-key`, `auth0-management-client-id`, `auth0-management-client-secret`, `stripe-secret-key`, `stripe-webhook-secret`, `stripe-monthly-price-id`, `stripe-annual-price-id`, and `foundry-api-key`. The last is a key from the newly created Azure OpenAI account. Use Test credentials and the existing Cosmos account key. A principal with Key Vault Secrets Officer access can set these using `az keyvault secret set`; do not put values in source control or command history. The API identity receives Key Vault Secrets Officer access to create and delete MCP credentials; the MCP identity receives Key Vault Secrets User access. Key Vault reference resolution can take time after secrets or role assignments change; restart the apps or refresh references when provisioning is complete.

Configure the GitHub `Test` environment before running the deployment workflow. Set variables `AZURE_FUNCTIONAPP_NAME`, `AZURE_MCP_FUNCTIONAPP_NAME`, `API_BASE_URL`, `MCP_ENDPOINT`, `APP_URL`, `SUPPORT_URL`, `AUTH0_DOMAIN`, `AUTH0_CLIENT_ID` and `AUTH0_AUDIENCE`. Use the deployment outputs for app names and URLs; `APP_URL` is the application SWA URL. Configure the corresponding Auth0 callback, logout and allowed origins, and Stripe webhook endpoint.

Set environment secrets `AZURE_FUNCTIONAPP_PUBLISH_PROFILE` and `AZURE_MCP_FUNCTIONAPP_PUBLISH_PROFILE` from each new Function App, plus `AZURE_STATIC_WEB_APPS_API_TOKEN` and `AZURE_MARKETING_STATIC_WEB_APPS_API_TOKEN` from the respective Static Web Apps. Publish profiles require SCM basic authentication; follow your organisation's credential policy. The workflow deploys all four applications on a push to `main` or manual dispatch. The workflow publishes browser configuration into public files, so those variables must not contain secrets.

The template configures CORS and the application origin for the generated application hostname. If using custom domains, update these settings and Auth0 configuration. The existing `mcp-functionapp.bicep` is a legacy standalone template, not used by this environment.
