param name string
param location string
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: { supportsHttpsTrafficOnly: true, minimumTlsVersion: 'TLS1_2', allowBlobPublicAccess: false }
}
resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}
resource deployments 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for app in ['api', 'mcp']: {
  parent: blobs
  name: 'deployments-${app}'
  properties: { publicAccess: 'None' }
}]
output name string = storage.name
output blobEndpoint string = storage.properties.primaryEndpoints.blob
