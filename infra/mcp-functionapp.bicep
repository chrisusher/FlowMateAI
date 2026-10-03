@description('Azure region for the MCP Function App.')
param location string = resourceGroup().location
@description('Globally unique MCP Function App name.')
param appName string
@description('Existing host and deployment storage connection string. Store this in the deployment system as a secret.')
@secure()
param storageConnectionString string
@description('Name of the storage account referenced by storageConnectionString.')
param storageAccountName string
@description('Existing Cosmos DB account endpoint.')
param cosmosEndpoint string
@description('Existing Cosmos DB account name.')
param cosmosAccountName string
@description('Existing Cosmos database name.')
param cosmosDatabaseName string
@description('OpenTelemetry collector endpoint, if configured.')
param otelEndpoint string = ''

var appInsightsName = '${appName}-appi'
resource hostStorage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' existing = {
  parent: hostStorage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'deployments'
  properties: { publicAccess: 'None' }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${appName}-flex'
  location: location
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: { Application_Type: 'web', IngestionMode: 'LogAnalytics' }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|10.0'
      minTlsVersion: '1.2'
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet-isolated' }
        { name: 'AzureWebJobsStorage', value: storageConnectionString }
        { name: 'DEPLOYMENT_STORAGE', value: storageConnectionString }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insights.properties.ConnectionString }
        { name: 'Database__AccountEndpoint', value: cosmosEndpoint }
        { name: 'Database__DatabaseName', value: cosmosDatabaseName }
        { name: 'OTEL_EXPORTER_OTLP_ENDPOINT', value: otelEndpoint }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: 'https://${hostStorage.name}.blob.${environment().suffixes.storage}/deployments'
          authentication: { type: 'StorageAccountConnectionString', storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE' }
        }
      }
      scaleAndConcurrency: { maximumInstanceCount: 40, instanceMemoryMB: 2048 }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
    }
  }
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmosAccountName
}

var cosmosReaderRole = '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000001'
var cosmosContributorRole = '${cosmos.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
var dataScope = '/dbs/${cosmosDatabaseName}/colls/'

resource workspaceReader 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, app.identity.principalId, 'WorkspaceDocuments', 'reader')
  properties: { principalId: app.identity.principalId, roleDefinitionId: cosmosReaderRole, scope: '${dataScope}WorkspaceDocuments' }
}
resource billingReader 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, app.identity.principalId, 'BillingEntitlements', 'reader')
  properties: { principalId: app.identity.principalId, roleDefinitionId: cosmosReaderRole, scope: '${dataScope}BillingEntitlements' }
}
resource keyReader 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, app.identity.principalId, 'McpKeys', 'reader')
  properties: { principalId: app.identity.principalId, roleDefinitionId: cosmosReaderRole, scope: '${dataScope}McpKeys' }
}
resource usageWriter 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmos
  name: guid(cosmos.id, app.identity.principalId, 'McpUsage', 'contributor')
  properties: { principalId: app.identity.principalId, roleDefinitionId: cosmosContributorRole, scope: '${dataScope}McpUsage' }
}

output principalId string = app.identity.principalId
output mcpEndpoint string = 'https://${app.properties.defaultHostName}/runtime/webhooks/mcp'
