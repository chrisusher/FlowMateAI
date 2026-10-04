@description('FlowMate identity environment. Use separate resource groups for test and production.')
@allowed([
  'test'
  'production'
])
param environmentName string

param location string = resourceGroup().location
param keycloakImage string
param containerRegistryName string
param keyVaultName string
param postgresServerName string
param postgresAdminUsername string = 'flowmateadmin'
@secure()
param postgresAdminPassword string
@secure()
param keycloakBootstrapPassword string
param deployKeycloak bool = true

var vnetName = 'flowmate-${environmentName}-identity-vnet'
var acaEnvironmentName = 'flowmate-${environmentName}-identity'
var keycloakAppName = 'flowmate-${environmentName}-keycloak'
var logWorkspaceName = 'flowmate-${environmentName}-identity-logs'
var registryName = containerRegistryName
var keyVaultResourceName = keyVaultName
var postgresDatabaseName = 'keycloak'
var postgresAdminSecretName = 'postgres-admin-password'
var keycloakAdminSecretName = 'keycloak-bootstrap-password'
var keycloakHostname = '${keycloakAppName}.${aca.properties.defaultDomain}'
var containerAppPullRole = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var keyVaultSecretsUserRole = '4633458b-17de-408a-b874-0445c86b69e6'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false, publicNetworkAccess: 'Enabled' }
}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultResourceName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enablePurgeProtection: true
    enabledForDeployment: false
    enabledForTemplateDeployment: false
    publicNetworkAccess: 'Enabled'
  }
}

resource dbSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: postgresAdminSecretName
  properties: { value: postgresAdminPassword }
}

resource adminSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: vault
  name: keycloakAdminSecretName
  properties: { value: keycloakBootstrapPassword }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logWorkspaceName
  location: location
  properties: { retentionInDays: 30, sku: { name: 'PerGB2018' } }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: { addressPrefixes: [ '10.80.0.0/16' ] }
    subnets: [
      {
        name: 'aca-infrastructure'
        properties: {
          addressPrefix: '10.80.0.0/23'
          delegations: [ { name: 'container-apps', properties: { serviceName: 'Microsoft.App/environments' } } ]
        }
      }
      {
        name: 'postgres'
        properties: {
          addressPrefix: '10.80.2.0/28'
          delegations: [ { name: 'postgres-flexible-server', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } } ]
        }
      }
    ]
  }
}

resource postgresDns 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.postgres.database.azure.com'
  location: 'global'
}

resource postgresDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: postgresDns
  name: '${vnetName}-link'
  location: 'global'
  properties: { registrationEnabled: false, virtualNetwork: { id: vnet.id } }
}

resource pgServer 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: location
  sku: { name: 'Standard_B2ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: postgresAdminUsername
    administratorLoginPassword: postgresAdminPassword
    authConfig: { activeDirectoryAuth: 'Disabled', passwordAuth: 'Enabled' }
    backup: { backupRetentionDays: 14, geoRedundantBackup: 'Disabled' }
    storage: { storageSizeGB: 32, autoGrow: 'Enabled' }
    network: {
      delegatedSubnetResourceId: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'postgres')
      privateDnsZoneArmResourceId: postgresDns.id
      publicNetworkAccess: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
  }
  dependsOn: [ postgresDnsLink ]
}

resource pgDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: pgServer
  name: postgresDatabaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

resource aca 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: acaEnvironmentName
  location: location
  properties: {
    vnetConfiguration: { infrastructureSubnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, 'aca-infrastructure') }
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: { customerId: workspace.properties.customerId, sharedKey: workspace.listKeys().primarySharedKey }
    }
  }
  dependsOn: [ vnet ]
}

resource imageIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${keycloakAppName}-identity'
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, imageIdentity.id, containerAppPullRole)
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', containerAppPullRole), principalId: imageIdentity.properties.principalId, principalType: 'ServicePrincipal' }
}

resource kvSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, imageIdentity.id, keyVaultSecretsUserRole)
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRole), principalId: imageIdentity.properties.principalId, principalType: 'ServicePrincipal' }
}

resource keycloak 'Microsoft.App/containerApps@2024-03-01' = if (deployKeycloak) {
  name: keycloakAppName
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${imageIdentity.id}': {} }
  }
  properties: {
    managedEnvironmentId: aca.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto', allowInsecure: false }
      registries: [ { server: registry.properties.loginServer, identity: imageIdentity.id } ]
      secrets: [
        { name: 'postgres-password', keyVaultUrl: '${vault.properties.vaultUri}secrets/${postgresAdminSecretName}', identity: imageIdentity.id }
        { name: 'bootstrap-password', keyVaultUrl: '${vault.properties.vaultUri}secrets/${keycloakAdminSecretName}', identity: imageIdentity.id }
      ]
    }
    template: {
      scale: { minReplicas: 1, maxReplicas: 1 }
      containers: [
        {
          name: 'keycloak'
          image: keycloakImage
          resources: { cpu: 1, memory: '2Gi' }
          env: [
            { name: 'KC_DB', value: 'postgres' }
            { name: 'KC_DB_URL_HOST', value: pgServer.properties.fullyQualifiedDomainName }
            { name: 'KC_DB_URL_DATABASE', value: postgresDatabaseName }
            { name: 'KC_DB_URL_PORT', value: '5432' }
            { name: 'KC_DB_URL_PROPERTIES', value: '?sslmode=verify-full' }
            { name: 'KC_DB_USERNAME', value: postgresAdminUsername }
            { name: 'KC_DB_PASSWORD', secretRef: 'postgres-password' }
            { name: 'KC_HOSTNAME', value: 'https://${keycloakHostname}' }
            { name: 'KC_PROXY_HEADERS', value: 'xforwarded' }
            { name: 'KC_HTTP_ENABLED', value: 'true' }
            { name: 'KC_HEALTH_ENABLED', value: 'true' }
            { name: 'KC_METRICS_ENABLED', value: 'true' }
            { name: 'KC_BOOTSTRAP_ADMIN_USERNAME', value: 'bootstrap-admin' }
            { name: 'KC_BOOTSTRAP_ADMIN_PASSWORD', secretRef: 'bootstrap-password' }
          ]
          probes: [
            { type: 'Startup', httpGet: { path: '/health/started', port: 9000, scheme: 'HTTP' }, initialDelaySeconds: 10, periodSeconds: 10, failureThreshold: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 9000, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 9000, scheme: 'HTTP' }, periodSeconds: 10, failureThreshold: 3 }
          ]
        }
      ]
    }
  }
  dependsOn: [ pgDatabase, acrPull, kvSecrets ]
}

output keycloakUrl string = 'https://${keycloakHostname}'
output apiAuthority string = 'https://${keycloakHostname}/realms/flowmate'
output registryLoginServer string = registry.properties.loginServer
output keyVaultUri string = vault.properties.vaultUri
output containerAppName string = keycloakAppName
