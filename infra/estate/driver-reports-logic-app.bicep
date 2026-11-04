// The P12 Driver reports Logic App: a Logic Apps Standard app that reads the Drivers' reports from its own subscription (sessions keyed by Trip
// number), calls the TMS for each as the Driver reports identity (for a Proof of delivery, after fetching its files from the Driver app's transit container), settles the message, and tells the Driver app what became of the report. Its
// workflow is deployed by azd's postdeploy hook (infra/deploy-driver-reports-logic-app.sh), because the app needs the addresses of the TMS and
// the Driver app, which exist only after Aspire deploys them. It needs no secret: it calls both as its managed identity.
//
// It shares the intake Logic App's plan: a second Workflow Standard plan would double the cost of the sandbox for nothing.
// Its storage, host identity and run history are its own.

param location string
param tags object
param integrationRetentionDays int

@description('The Driver reports identity: holds the Trip start role on the TMS and the outcome role on the Driver app, and reads its subscription. The Logic App runs its steps as it.')
param driverReportsIdentityName string

@description('The TMS API app registration, the audience of the tokens the Logic App gets for the TMS.')
param tmsApiClientId string

@description('The TMS API\'s address, empty until the apps are first deployed. The postdeploy hook sets it.')
param tmsBaseUrl string

@description('The Driver app API\'s address, empty until the apps are first deployed. The postdeploy hook sets it.')
param driverAppBaseUrl string

@description('The Driver app API app registration, the audience of the tokens the Logic App gets for the Driver app\'s outcome endpoint.')
param driverAppApiClientId string

@description('The Driver app\'s transit container for the files of a Proof of delivery, and the address of its storage account\'s blob service.')
param driverProofsContainerName string
param driverProofsBlobEndpoint string

param applicationInsightsName string

@description('The Service Bus namespace the Driver reports subscription is in.')
param serviceBusName string

@description('The App Service plan of the intake Logic App, which this app shares.')
param planName string

@description('The Key Vault that holds the Logic Apps\' content connections.')
param contentKeyVaultName string

@description('The Integration console\'s managed identity, which reads the app\'s run history.')
param integrationConsolePrincipalId string

@description('The Partner portal managed identity, which the presenter panel fault uses to disable this Logic App and enable it again.')
param partnerPortalPrincipalId string

@description('The custom role that can only read, start and stop a site (logic-app-switch-role.bicep).')
param logicAppSwitchRoleId string

var resourceToken = uniqueString(resourceGroup().id)
var logicAppName = 'logic-driver-reports-${resourceToken}'
var contentShareName = logicAppName
var serviceBusApiId = subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'servicebus')

resource driverReportsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: driverReportsIdentityName
}

// The Logic App's own identity, for its storage and the Key Vault reference, so the Driver reports identity holds nothing but
// what the workflow's steps need.
resource hostIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-driver-reports-host-${resourceToken}'
  location: location
  tags: tags
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusName
}

resource plan 'Microsoft.Web/serverfarms@2024-11-01' existing = {
  name: planName
}

resource contentKeyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: contentKeyVaultName
}

// Logic Apps Standard keeps its state and run history here. It reaches blobs, queues and tables as its host identity.
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stdrv${resourceToken}'
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
    // The app's content share on the Workflow Standard plan still needs a key; it is read from Key Vault, never kept
    // in the app's settings.
    allowSharedKeyAccess: true
  }
}

resource fileServices 'Microsoft.Storage/storageAccounts/fileServices@2024-01-01' = {
  parent: storage
  name: 'default'
}

// Created here because the app skips checking the content share, which it can't do through a Key Vault reference.
resource contentShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2024-01-01' = {
  parent: fileServices
  name: contentShareName
}

var storageRoles = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
  '17d1049b-9a84-46fb-8f53-869881c3d3ab' // Storage Account Contributor
]

resource storageAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in storageRoles: {
    name: guid(storage.id, hostIdentity.id, role)
    scope: storage
    properties: {
      principalId: hostIdentity.properties.principalId
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
      principalType: 'ServicePrincipal'
    }
  }
]

resource contentConnection 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  parent: contentKeyVault
  name: 'driver-reports-logic-app-content'
  properties: {
    value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
  }
}

var keyVaultSecretsUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource contentConnectionAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(contentConnection.id, hostIdentity.id, keyVaultSecretsUser)
  scope: contentConnection
  properties: {
    principalId: hostIdentity.properties.principalId
    roleDefinitionId: keyVaultSecretsUser
    principalType: 'ServicePrincipal'
  }
}

// The Service Bus managed connector, which the workflow reads the Driver reports subscription with. The built-in Service Bus trigger fails in this app
// before it creates a run, with no error to read; the managed connector says why when it fails. The connection signs in as the
// app's identity, which holds Azure Service Bus Data Receiver on what it reads, and the access policy lets that identity use it.
resource serviceBusConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'servicebus-driver-reports'
  location: location
  tags: tags
  #disable-next-line BCP187 // the connection's type lacks the kind a Standard logic app's connection needs
  kind: 'V2'
  properties: {
    displayName: 'Service Bus'
    api: {
      id: serviceBusApiId
    }
    #disable-next-line BCP089 // the type lacks the authentication set a managed identity connection uses
    parameterValueSet: {
      name: 'managedIdentityAuth'
      values: {
        namespaceEndpoint: {
          value: 'sb://${serviceBus.name}.servicebus.windows.net/'
        }
      }
    }
  }
}

#disable-next-line BCP081 // no type definitions are published for the access policy
resource serviceBusConnectionAccess 'Microsoft.Web/connections/accessPolicies@2016-06-01' = {
  parent: serviceBusConnection
  name: 'driver-reports-identity'
  properties: {
    principal: {
      type: 'ActiveDirectory'
      identity: {
        tenantId: tenant().tenantId
        objectId: driverReportsIdentity.properties.principalId
      }
    }
  }
}

resource logicApp 'Microsoft.Web/sites@2024-11-01' = {
  name: logicAppName
  location: location
  tags: tags
  kind: 'functionapp,workflowapp'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${hostIdentity.id}': {}
      '${driverReportsIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: hostIdentity.id
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      // Needed when the app reaches its storage with a managed identity, and for a trigger that polls Service Bus.
      functionsRuntimeScaleMonitoringEnabled: true
      appSettings: [
        { name: 'APP_KIND', value: 'workflowApp' }
        { name: 'FUNCTIONS_EXTENSION_VERSION', value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME', value: 'dotnet' }
        { name: 'WEBSITE_NODE_DEFAULT_VERSION', value: '~22' }
        { name: 'AzureFunctionsJobHost__extensionBundle__id', value: 'Microsoft.Azure.Functions.ExtensionBundle.Workflows' }
        { name: 'AzureFunctionsJobHost__extensionBundle__version', value: '[1.*, 2.0.0)' }
        { name: 'AzureWebJobsStorage__blobServiceUri', value: storage.properties.primaryEndpoints.blob }
        { name: 'AzureWebJobsStorage__queueServiceUri', value: storage.properties.primaryEndpoints.queue }
        { name: 'AzureWebJobsStorage__tableServiceUri', value: storage.properties.primaryEndpoints.table }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__managedIdentityResourceId', value: hostIdentity.id }
        { name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING', value: '@Microsoft.KeyVault(SecretUri=${contentConnection.properties.secretUri})' }
        { name: 'WEBSITE_CONTENTSHARE', value: contentShareName }
        { name: 'WEBSITE_SKIP_CONTENTSHARE_VALIDATION', value: '1' }
        // Where the runs' telemetry goes: the estate's Application Insights (coding guidelines §7).
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: applicationInsights.properties.ConnectionString }
        // Run history keeps as long as the rest of the integration layer's traces; Logic Apps keeps it for 365 days at most.
        { name: 'Workflows.RuntimeConfiguration.RetentionInDays', value: string(min(integrationRetentionDays, 365)) }
        // The workflow's parameters and connections (parameters.json, connections.json). There is no secret among them: the workflow calls
        // the TMS and the Driver app as its managed identity.
        { name: 'TMS_BASE_URL', value: tmsBaseUrl }
        { name: 'TMS_API_CLIENT_ID', value: tmsApiClientId }
        { name: 'DRIVER_APP_BASE_URL', value: driverAppBaseUrl }
        { name: 'DRIVER_APP_API_CLIENT_ID', value: driverAppApiClientId }
        { name: 'DRIVER_REPORTS_IDENTITY_ID', value: driverReportsIdentity.id }
        { name: 'DRIVER_PROOFS_CONTAINER', value: driverProofsContainerName }
        { name: 'DRIVER_PROOFS_BLOB_ENDPOINT', value: driverProofsBlobEndpoint }
        // The Service Bus managed connection (connections.json).
        { name: 'SERVICE_BUS_API_ID', value: serviceBusApiId }
        { name: 'SERVICE_BUS_CONNECTION_ID', value: serviceBusConnection.id }
        #disable-next-line use-resource-symbol-reference // the connection's type doesn't declare its runtime URL
        { name: 'SERVICE_BUS_CONNECTION_RUNTIME_URL', value: reference(serviceBusConnection.id, '2016-06-01', 'Full').properties.connectionRuntimeUrl }
      ]
    }
  }
  dependsOn: [
    storageAccess
    contentConnectionAccess
    contentShare
    serviceBusConnectionAccess
  ]
}

// The Integration console opens a run's steps and their bodies, as its own identity (ADR 0017). Logic Apps Standard
// Reader lets it read the app, its workflows and their run history, and change nothing: it never resubmits a run.
var logicAppsStandardReader = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4accf36b-2c05-432f-91c8-5c532dff4c73')

resource integrationConsoleReadsRunHistory 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(logicApp.id, integrationConsolePrincipalId, logicAppsStandardReader)
  scope: logicApp
  properties: {
    principalId: integrationConsolePrincipalId
    roleDefinitionId: logicAppsStandardReader
    principalType: 'ServicePrincipal'
  }
}

// The Partner portal's identity may switch this one Logic App off and on (the fault driver-reports-logic-app-disabled) and do nothing else to it.
resource partnerPortalSwitchesLogicApp 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(logicApp.id, partnerPortalPrincipalId, logicAppSwitchRoleId)
  scope: logicApp
  properties: {
    principalId: partnerPortalPrincipalId
    roleDefinitionId: logicAppSwitchRoleId
    principalType: 'ServicePrincipal'
  }
}

output logicAppName string = logicApp.name
