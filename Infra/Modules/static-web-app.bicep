@description('Azure Static Web App name for the FlowMateAI test environment.')
param staticWebAppName string = 'flowmateai-test'

@description('Azure region for the Static Web App resource.')
param location string = 'westeurope'

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: staticWebAppName
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

output defaultHostname string = staticWebApp.properties.defaultHostname
