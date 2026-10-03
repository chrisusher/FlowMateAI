@description('Web Static Web App resource name.')
param staticWebAppName string = 'flowmateai-test'

@description('Azure Static Web Apps region.')
param staticWebAppLocation string = 'westeurope'

@description('Native Linux Azure Functions app name.')
param functionAppName string = 'flowmateai-test-api'

@description('Browser origin allowed to call the TEST API.')
param allowedWebOrigin string = 'https://gentle-meadow-054b0dd03.4.azurestaticapps.net'

module staticWebApp '../../Modules/static-web-app.bicep' = {
  name: 'flowmateai-test-static-web-app'
  params: {
    staticWebAppName: staticWebAppName
    location: staticWebAppLocation
  }
}

module functionApp '../../Modules/function-app.bicep' = {
  name: 'flowmateai-test-function-app'
  params: {
    functionAppName: functionAppName
    allowedWebOrigin: allowedWebOrigin
  }
}

output staticWebAppHostname string = staticWebApp.outputs.defaultHostname
output functionHostName string = functionApp.outputs.functionHostName
