param accountName string
param databaseName string

resource account 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: accountName
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: account
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

// Partition paths are case-sensitive and follow the Cosmos JSON property names.
var containers = [
  { name: 'BillingEntitlements', partitionKey: '/UserId' }
  { name: 'McpKeys', partitionKey: '/UserId' }
  { name: 'McpUsage', partitionKey: '/userId', defaultTtl: 172800 }
  { name: 'StripeEvents', partitionKey: '/PartitionId' }
  { name: 'Users', partitionKey: '/UserId' }
  { name: 'WorkspaceDocuments', partitionKey: '/UserId' }
  { name: 'WorkspaceRecords', partitionKey: '/UserId' }
]

resource container 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = [
  for item in containers: {
    parent: database
    name: item.name
    properties: {
      resource: union(
        {
          id: item.name
          partitionKey: {
            paths: [item.partitionKey]
            kind: 'Hash'
          }
        },
        contains(item, 'defaultTtl') ? { defaultTtl: item.defaultTtl } : {}
      )
    }
  }
]
