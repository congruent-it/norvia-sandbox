// The P7 Consignee notification Logic App: a Logic Apps Standard app that picks up ConsigneeNotificationRequested from its own subscription,
// reads the Consignee's email address and tracking code from the TMS as the Consignee notification identity, makes the email with the link and
// sends it through the email resource's REST API. Its workflow and local functions are deployed by azd's postdeploy hook
// (infra/deploy-consignee-notification-logic-app.ps1), because the app needs the addresses of the TMS and the Partner portal, which exist
// only after Aspire deploys them.
//
// It shares the intake Logic App's plan: a second Workflow Standard plan would double the cost of the sandbox for nothing.
// Its storage, host identity and run history are its own. It reads no Key Vault of its own: the address and the code come from the TMS,
// so neither is kept anywhere else (ADR 0052).

param location string
param tags object
param integrationRetentionDays int

@description('The Consignee notification identity: the one principal holding the Consignee notification read role on the TMS, and the one that reads its subscription and sends through the email resource. The Logic App runs its steps as it.')
param consigneeNotificationIdentityName string

@description('The TMS API app registration, the audience of the tokens the Logic App gets for the TMS.')
param tmsApiClientId string

@description('The TMS API\'s address, empty until the apps are first deployed. The postdeploy hook sets it.')
param tmsBaseUrl string

@description('The Partner portal\'s address, which the link in the email is on; empty until the apps are first deployed. The postdeploy hook sets it.')
param partnerPortalBaseUrl string

@description('The email resource\'s endpoint (Azure Communication Services Email, such as https://<name>.communication.azure.com), which the workflow posts the email to. Empty until an operator has one: then the run fails at the send and says why.')
param emailEndpoint string

@description('The sender address the email resource has verified for the sandbox, such as DoNotReply@<domain>.')
param emailSenderAddress string

param applicationInsightsName string

@description('The Service Bus namespace the Consignee notification subscription is in.')
param serviceBusName string

@description('The App Service plan of the intake Logic App, which this app shares.')
param planName string

@description('The Key Vault that holds the Logic Apps\' content connections.')
param contentKeyVaultName string

@description('The Integration console\'s managed identity, which reads the app\'s run history.')
param integrationConsolePrincipalId string

var resourceToken = uniqueString(resourceGroup().id)
var logicAppName = 'logic-consignee-notification-${resourceToken}'
var contentShareName = logicAppName
var serviceBusApiId = subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'servicebus')

resource consigneeNotificationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: consigneeNotificationIdentityName
}

// The Logic App's own identity, for its storage and the Key Vault reference, so the Consignee notification identity holds nothing but
// what the workflow's steps need.
resource hostIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-consignee-notification-host-${resourceToken}'
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
  name: 'stconsnotif${resourceToken}'
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
  name: 'consignee-notification-logic-app-content'
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

// The Service Bus managed connector, which the workflow reads the Consignee notification subscription with. The built-in Service Bus trigger fails in this app
// before it creates a run, with no error to read; the managed connector says why when it fails. The connection signs in as the
// app's identity, which holds Azure Service Bus Data Receiver on what it reads, and the access policy lets that identity use it.
resource serviceBusConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'servicebus-consignee-notification'
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
  name: 'consignee-notification-identity'
  properties: {
    principal: {
      type: 'ActiveDirectory'
      identity: {
        tenantId: tenant().tenantId
        objectId: consigneeNotificationIdentity.properties.principalId
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
      '${consigneeNotificationIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: hostIdentity.id
    siteConfig: {
      // The Consignee notification map is a .NET 8 local function (ADR 0019).
      netFrameworkVersion: 'v8.0'
      use32BitWorkerProcess: false
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
        // The workflow's parameters and connections (parameters.json, connections.json). The Consignee's address and the tracking code are
        // not among them: the workflow asks the TMS for both, and never keeps either.
        { name: 'TMS_BASE_URL', value: tmsBaseUrl }
        { name: 'TMS_API_CLIENT_ID', value: tmsApiClientId }
        { name: 'PARTNER_PORTAL_BASE_URL', value: partnerPortalBaseUrl }
        { name: 'EMAIL_ENDPOINT', value: emailEndpoint }
        { name: 'EMAIL_SENDER_ADDRESS', value: emailSenderAddress }
        { name: 'CONSIGNEE_NOTIFICATION_IDENTITY_ID', value: consigneeNotificationIdentity.id }
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

output logicAppName string = logicApp.name
