// The claim check's transit for a Carrier's Proof of delivery (ADR 0009): a storage account with the inbound container, where
// API Management stores the body unparsed and the Carrier Proof of delivery Logic App fetches it from and deletes it from once
// the TMS accepted it. The TMS's own container for the files lives with the apps, in the storage account Aspire provisions
// (ADR 0002), with its tiering rule. This one is only transit: a blob whose message failed stays behind for investigation,
// as long as the run history and the trail of its message (integrationRetentionDays).

param location string
param tags object
param integrationRetentionDays int

@description('API Management\'s identity, which writes the Proofs of delivery here and nothing else.')
param apiManagementPrincipalId string

@description('The Carrier Proof of delivery Logic App\'s identity, which reads and deletes the Proofs of delivery here and nothing else.')
param proofOfDeliveryPrincipalId string

@description('The presenter\'s Entra object ID, empty for none. The presenter may see whether a Proof of delivery\'s blob is still here, as the presenter smoke run does.')
param presenterPrincipalId string = ''

@description('The as-found toggle noInboundLifecycleRule: build no lifecycle rule, so processed blobs and the ones left behind are never deleted (P6).')
param noLifecycleRule bool = false

@description('The as-found toggle noMalwareScanning: build no Defender for Storage malware scanning, so files from outside reach the TMS unscanned (P6).')
param noMalwareScanning bool = false

var containerName = 'inbound-proofs-of-delivery'

resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stinbound${uniqueString(resourceGroup().id)}'
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

resource blobServices 'Microsoft.Storage/storageAccounts/blobServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

resource inbound 'Microsoft.Storage/storageAccounts/blobServices/containers@2024-01-01' = {
  parent: blobServices
  name: containerName
}

// Deletes a blob after the estate's retention, the same as the run history and the trail of its message. Absent with the
// as-found toggle, never disabled.
resource retention 'Microsoft.Storage/storageAccounts/managementPolicies@2024-01-01' = if (!noLifecycleRule) {
  parent: storage
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'inbound-proofs-of-delivery-expire'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${containerName}/'
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
    inbound
  ]
}

// Defender for Storage scans each blob on upload and tags the result on the blob. It is billed per storage account per month and per
// GB scanned (docs/operations/as-found.md), so the monthly cap is low: the sandbox's Proofs of delivery are a few small files.
resource malwareScanning 'Microsoft.Security/defenderForStorageSettings@2022-12-01-preview' = if (!noMalwareScanning) {
  name: 'current'
  scope: storage
  properties: {
    isEnabled: true
    malwareScanning: {
      onUpload: {
        isEnabled: true
        capGBPerMonth: 10
      }
    }
    sensitiveDataDiscovery: {
      isEnabled: false
    }
    overrideSubscriptionLevelSettings: true
  }
}

var storageBlobDataContributor = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

// Scoped to the container, so API Management writes nothing else and the Logic App reads nothing else.
resource apiManagementStoresProofsOfDelivery 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(inbound.id, apiManagementPrincipalId, storageBlobDataContributor)
  scope: inbound
  properties: {
    principalId: apiManagementPrincipalId
    roleDefinitionId: storageBlobDataContributor
    principalType: 'ServicePrincipal'
  }
}

resource proofOfDeliveryFetchesAndDeletesItsBlobs 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(inbound.id, proofOfDeliveryPrincipalId, storageBlobDataContributor)
  scope: inbound
  properties: {
    principalId: proofOfDeliveryPrincipalId
    roleDefinitionId: storageBlobDataContributor
    principalType: 'ServicePrincipal'
  }
}

var storageBlobDataReader = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1')

// Reads only: the presenter looks at what is left behind, and never changes it.
resource presenterSeesProofsOfDelivery 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(presenterPrincipalId)) {
  name: guid(inbound.id, presenterPrincipalId, storageBlobDataReader)
  scope: inbound
  properties: {
    principalId: presenterPrincipalId
    roleDefinitionId: storageBlobDataReader
    principalType: 'User'
  }
}

output storageAccountName string = storage.name
output containerName string = inbound.name
output blobEndpoint string = storage.properties.primaryEndpoints.blob
