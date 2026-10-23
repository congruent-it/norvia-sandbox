// What the Driver app's Trip endpoint asks of its caller, the P11 Trip dispatch Logic App: a credential, which the Driver app
// and the Logic App read from this Key Vault. The Driver app itself is hosted by Aspire with the other apps (ADR 0004); the estate
// owns the vault and the identities that read it. It also owns the transit container for the files of a Proof of delivery (P12, ADR 0009): the Driver
// app stores them there, the Driver reports Logic App fetches them and deletes them once the TMS accepted the Proof of delivery, and a lifecycle rule
// deletes what is left after the estate's retention (integrationRetentionDays), the same as the run history and the trail of the message.

param location string
param tags object
param integrationRetentionDays int

@secure()
@description('A random value the credential is derived from, the one the Carrier endpoints\' credentials come from.')
param endpointKeySeed string

@description('The Driver app\'s identity, which reads the credential its Trip endpoint asks for.')
param driverAppPrincipalId string

@description('The Driver reports Logic App\'s identity, which fetches the Proofs of delivery\'s files from the transit container and deletes them, and nothing else.')
param driverReportsPrincipalId string

@description('The P11 Trip dispatch Logic App\'s identity, which reads the same credential and nothing else in the vault.')
param tripDispatchPrincipalId string

@description('The as-found toggle noTransitLifecycleRule: build no lifecycle rule, so the files are never deleted (P12).')
param noLifecycleRule bool = false

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

var transitContainerName = 'driver-proofs-of-delivery'

resource transitStorage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stdriver${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    // Everyone reaches it with a managed identity; there is no key to use.
    allowSharedKeyAccess: false
  }
}

resource transitBlobServices 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: transitStorage
  name: 'default'
}

resource transit 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: transitBlobServices
  name: transitContainerName
}

// Deletes a file after the estate's retention: a Proof of delivery whose files were never fetched, or whose message failed, stays for investigation until then.
resource transitRetention 'Microsoft.Storage/storageAccounts/managementPolicies@2024-01-01' = if (!noLifecycleRule) {
  parent: transitStorage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'driver-proofs-of-delivery-expire'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${transitContainerName}/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: integrationRetentionDays
                }
              }
            }
          }
        }
      ]
    }
  }
  dependsOn: [
    transit
  ]
}

var storageBlobDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

// Scoped to the container, so the Driver app stores nothing else and the Logic App reads and deletes nothing else.
resource driverAppStoresProofsOfDelivery 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(transit.id, driverAppPrincipalId, storageBlobDataContributor)
  scope: transit
  properties: {
    principalId: driverAppPrincipalId
    roleDefinitionId: storageBlobDataContributor
    principalType: 'ServicePrincipal'
  }
}

resource driverReportsFetchesAndDeletesTheFiles 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(transit.id, driverReportsPrincipalId, storageBlobDataContributor)
  scope: transit
  properties: {
    principalId: driverReportsPrincipalId
    roleDefinitionId: storageBlobDataContributor
    principalType: 'ServicePrincipal'
  }
}

output storageAccountName string = transitStorage.name
output containerName string = transit.name
output blobEndpoint string = transitStorage.properties.primaryEndpoints.blob
