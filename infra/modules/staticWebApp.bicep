param name string
param location string

resource app 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  sku: { name: 'Free', tier: 'Free' }
  properties: { allowConfigFileUpdates: true }
}

output hostname string = app.properties.defaultHostname
