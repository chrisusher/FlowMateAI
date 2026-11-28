param vaultName string
param principalId string

@allowed([
  'user'
  'officer'
])
@description('The officer option grants secret reads plus the custom MCP credential set/delete role; it does not grant the built-in Secrets Officer role.')
param accessRole string = 'user'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: vaultName
}

// Assignability is limited to this resource group; the assignment below is limited to the vault.
resource credentialWriter 'Microsoft.Authorization/roleDefinitions@2022-04-01' = if (accessRole == 'officer') {
  name: guid(resourceGroup().id, 'flowmate-mcp-credential-writer')
  properties: {
    roleName: 'FlowMate MCP credential writer ${uniqueString(resourceGroup().id)}'
    description: 'Set and delete assistant credential secrets.'
    type: 'CustomRole'
    assignableScopes: [resourceGroup().id]
    permissions: [
      {
        actions: []
        notActions: []
        dataActions: [
          'Microsoft.KeyVault/vaults/secrets/setSecret/action'
          'Microsoft.KeyVault/vaults/secrets/delete'
        ]
        notDataActions: []
      }
    ]
  }
}

resource readerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, principalId, 'key-vault-secrets-user')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource writerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (accessRole == 'officer') {
  scope: vault
  name: guid(vault.id, principalId, 'flowmate-mcp-credential-writer')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', credentialWriter.name)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
