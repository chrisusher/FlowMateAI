param name string
param location string
param storageAccountName string
param deploymentUrl string
param insightsConnectionString string
param appSettings array

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = { name: storageAccountName }

var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: '${name}-plan'
  location: location
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: name
  location: location
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: concat(
        [
          { name: 'AzureWebJobsStorage', value: storageConnectionString }
          { name: 'DEPLOYMENT_STORAGE', value: storageConnectionString }
          { name: 'ConnectionStrings__Storage', value: storageConnectionString }
          { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insightsConnectionString }
        ],
        appSettings
      )
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: deploymentUrl
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE'
          }
        }
      }
      runtime: { name: 'dotnet-isolated', version: '11.0' }
      scaleAndConcurrency: { maximumInstanceCount: 40, instanceMemoryMB: 2048 }
    }
  }
}

output principalId string = app.identity.principalId
output hostname string = app.properties.defaultHostName
