// What the Driver app's Trip endpoint asks of its caller, the P11 Trip dispatch Logic App: a credential, which the Driver app
// and the Logic App read from this Key Vault. The Driver app itself is hosted by Aspire with the other apps (ADR 0004); the estate
// owns the vault and the identities that read it.

param location string
param tags object

@secure()
@description('A random value the credential is derived from, the one the Carrier endpoints\' credentials come from.')
param endpointKeySeed string

@description('The Driver app\'s identity, which reads the credential its Trip endpoint asks for.')
param driverAppPrincipalId string

@description('The P11 Trip dispatch Logic App\'s identity, which reads the same credential and nothing else in the vault.')
param tripDispatchPrincipalId string

resource driverAppSecrets 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: 'kv-driver-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
  }
}

// Derived from the seed, as 64 hex digits from two GUIDs. Nobody reads the seed back out of the vault.
resource endpointKey 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: driverAppSecrets
  name: 'driver-app-endpoint-key'
  properties: {
    value: replace('${guid(endpointKeySeed, 'driver-app-endpoint-key')}${guid('driver-app-endpoint-key', endpointKeySeed)}', '-', '')
  }
}

var keyVaultSecretsUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

// Scoped to the secret, so neither reads anything else in the vault.
resource driverAppReadsTheKey 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(endpointKey.id, driverAppPrincipalId, keyVaultSecretsUser)
  scope: endpointKey
  properties: {
    principalId: driverAppPrincipalId
    roleDefinitionId: keyVaultSecretsUser
    principalType: 'ServicePrincipal'
  }
}

resource tripDispatchReadsTheKey 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(endpointKey.id, tripDispatchPrincipalId, keyVaultSecretsUser)
  scope: endpointKey
  properties: {
    principalId: tripDispatchPrincipalId
    roleDefinitionId: keyVaultSecretsUser
    principalType: 'ServicePrincipal'
  }
}

output keyVaultName string = driverAppSecrets.name
