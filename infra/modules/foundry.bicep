param name string
param location string
@description('Azure OpenAI model name available in the chosen region.')
param modelName string
param modelVersion string
param deploymentName string
@minValue(1)
param capacity int = 1
resource account 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: name
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: { customSubDomainName: name, publicNetworkAccess: 'Enabled' }
}
resource deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: account
  name: deploymentName
  sku: { name: 'GlobalStandard', capacity: capacity }
  properties: { model: { format: 'OpenAI', name: modelName, version: modelVersion } }
}
output endpoint string = account.properties.endpoint
output name string = account.name
