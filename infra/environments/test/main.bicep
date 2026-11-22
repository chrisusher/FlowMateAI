targetScope = 'resourceGroup'

@description('Short, globally unique lowercase alphanumeric prefix.')
@minLength(3)
@maxLength(10)
param prefix string = 'flowmate'
param location string = resourceGroup().location

@description('A region supported by Azure Static Web Apps.')
param staticWebAppLocation string = 'westeurope'

@description('Existing Cosmos account endpoint. No Cosmos resources are created.')
param cosmosEndpoint string
param cosmosDatabaseName string = 'flowmate-Test'
param auth0Authority string
param auth0Audience string

param foundryDeployment string = 'coach'

var suffix = uniqueString(resourceGroup().id)
var baseName = '${prefix}-${suffix}'
var vaultName = '${prefix}${take(suffix, 10)}kv'

module storage '../../modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: '${prefix}${take(suffix, 13)}'
    location: location
  }
}

module vault '../../modules/keyVault.bicep' = {
  name: 'vault'
  params: {
    name: vaultName
    location: location
  }
}

module monitoring '../../modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    name: baseName
    location: location
  }
}

module foundry '../../modules/foundry.bicep' = {
  name: 'foundry'
  params: {
    name: '${baseName}-ai'
    location: location
    deploymentName: foundryDeployment
  }
}

module web '../../modules/staticWebApp.bicep' = {
  name: 'web'
  params: {
    name: '${baseName}-app'
    location: staticWebAppLocation
  }
}

module marketing '../../modules/staticWebApp.bicep' = {
  name: 'marketing'
  params: {
    name: '${baseName}-marketing'
    location: staticWebAppLocation
  }
}

var commonSettings = [
  {
    name: 'Database__AccountEndpoint'
    value: cosmosEndpoint
  }
  {
    name: 'Database__DatabaseName'
    value: cosmosDatabaseName
  }
  {
    name: 'Database__Key'
    value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/cosmos-key/)'
  }
  {
    name: 'Global__Environment'
    value: 'Test'
  }
]

module api '../../modules/functionApp.bicep' = {
  name: 'api'
  params: {
    name: '${baseName}-api'
    location: location
    storageAccountName: storage.outputs.name
    deploymentUrl: '${storage.outputs.blobEndpoint}deployments-api'
    insightsConnectionString: monitoring.outputs.connectionString
    appSettings: concat(commonSettings, [
      {
        name: 'KeyVault__VaultUri'
        value: vault.outputs.uri
      }
      {
        name: 'Cors__AllowedOrigins__0'
        value: 'https://${web.outputs.hostname}'
      }
      {
        name: 'WebApp__Origin'
        value: 'https://${web.outputs.hostname}'
      }
      {
        name: 'Auth0__Authority'
        value: auth0Authority
      }
      {
        name: 'Auth0__Audience'
        value: auth0Audience
      }
      {
        name: 'Auth0__ManagementClientId'
        value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/auth0-management-client-id/)'
      }
      {
        name: 'Auth0__ManagementClientSecret'
        value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/auth0-management-client-secret/)'
      }
      {
        name: 'Stripe__SecretKey'
        value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/stripe-secret-key/)'
      }
      {
        name: 'Stripe__WebhookSecret'
        value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/stripe-webhook-secret/)'
      }
      {
        name: 'Stripe__MonthlyPriceId'
        value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/stripe-monthly-price-id/)'
      }
      {
        name: 'Stripe__AnnualPriceId'
        value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/stripe-annual-price-id/)'
      }
      {
        name: 'Foundry__Endpoint'
        value: foundry.outputs.endpoint
      }
      {
        name: 'Foundry__Deployment'
        value: foundryDeployment
      }
      {
        name: 'Foundry__ApiKey'
        value: '@Microsoft.KeyVault(SecretUri=${vault.outputs.uri}secrets/foundry-api-key/)'
      }
    ])
  }
}

module mcp '../../modules/functionApp.bicep' = {
  name: 'mcp'
  params: {
    name: '${baseName}-mcp'
    location: location
    storageAccountName: storage.outputs.name
    deploymentUrl: '${storage.outputs.blobEndpoint}deployments-mcp'
    insightsConnectionString: monitoring.outputs.connectionString
    appSettings: commonSettings
  }
}

module apiVaultAccess '../../modules/vaultAccess.bicep' = {
  name: 'api-vault-access'
  params: {
    vaultName: vaultName
    principalId: api.outputs.principalId
    accessRole: 'officer'
  }
}

module mcpVaultAccess '../../modules/vaultAccess.bicep' = {
  name: 'mcp-vault-access'
  params: {
    vaultName: vaultName
    principalId: mcp.outputs.principalId
  }
}

output apiName string = '${baseName}-api'
output mcpName string = '${baseName}-mcp'
output apiUrl string = 'https://${api.outputs.hostname}'
output mcpUrl string = 'https://${mcp.outputs.hostname}/runtime/webhooks/mcp'
output webName string = '${baseName}-app'
output webUrl string = 'https://${web.outputs.hostname}'
output marketingName string = '${baseName}-marketing'
output marketingUrl string = 'https://${marketing.outputs.hostname}'
output keyVaultName string = vaultName
output foundryName string = foundry.outputs.name
