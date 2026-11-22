param vaultName string
param principalId string

@allowed([
  'user'
  'officer'
])
param accessRole string = 'user'

var roleName = accessRole == 'officer' ? 'key-vault-secrets-officer' : 'key-vault-secrets-user'
var roleId = accessRole == 'officer' ? 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7' : '4633458b-17de-408a-b874-0445c86b69e6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = { name: vaultName }

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, principalId, roleName)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}
