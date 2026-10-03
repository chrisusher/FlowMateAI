@description('Name of the native, code-based Azure Functions app.')
param functionAppName string = 'flowmateai-test-api'

@description('Name of the Linux Flex Consumption plan.')
param hostingPlanName string = 'flowmateai-test-func-plan'

@description('Existing resource names provisioned by Aspire.')
param storageAccountName string = 'storage6dsvtskc45ed2'
param cosmosAccountName string = 'cosmosdb-6dsvtskc45ed2'
param apiIdentityName string = 'API_identity-6dsvtskc45ed2'

@description('The deployed Blazor Static Web App origin allowed to call this API.')
param allowedWebOrigin string = 'https://gentle-meadow-054b0dd03.4.azurestaticapps.net'

@description('Application environment label.')
param environmentName string = 'TEST'

@description('Cosmos DB database used by the API.')
param databaseName string = 'flowmate'

param deploymentContainerName string = 'flowmateai-test-api-deployments'
var deploymentStorageAccountName = 'fmtestdep${uniqueString(resourceGroup().id)}'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

var storageKey = storageAccount.listKeys().keys[0].value

var storageAccountKeys = 'DefaultEndpointsProtocol=https;AccountName=${deploymentStorageAccount.name};AccountKey=${storageKey};EndpointSuffix=${environment().suffixes.storage}'

resource deploymentStorageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: deploymentStorageAccountName
  location: resourceGroup().location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
  }
}

resource deploymentBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: deploymentStorageAccount
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: deploymentBlobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmosAccountName
}

var cosmosKey = cosmosAccount.listKeys().primaryMasterKey

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: apiIdentityName
}

resource hostingPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: hostingPlanName
  location: resourceGroup().location
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: functionAppName
  location: resourceGroup().location
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${deploymentStorageAccount.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 100
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
    }
  }
  dependsOn: [
    deploymentContainer
  ]
}

resource functionAppSettings 'Microsoft.Web/sites/config@2023-12-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    AzureWebJobsStorage__accountName: storageAccount.name
    AzureWebJobsStorage__credential: 'managedidentity'
    AzureWebJobsStorage__clientId: apiIdentity.properties.clientId
    DEPLOYMENT_STORAGE_CONNECTION_STRING: storageAccountKeys
    Storage__BlobServiceUri: storageAccount.properties.primaryEndpoints.blob
    Storage__ManagedIdentityClientId: apiIdentity.properties.clientId
    Database__AccountEndpoint: cosmosAccount.properties.documentEndpoint
    Database__Key: cosmosKey
    Database__DatabaseName: databaseName
    Global__Environment: environmentName
    Cors__AllowedOrigins__0: allowedWebOrigin
  }
}

output functionAppName string = functionApp.name
output functionHostName string = functionApp.properties.defaultHostName
output deploymentContainerName string = deploymentContainer.name
output deploymentStorageAccountName string = deploymentStorageAccount.name
