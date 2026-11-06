// The E2 Carrier invoice Logic App: a Logic Apps Standard app that API Management calls with a Carrier's invoice. It maps the invoice
// to the Canonical Model, receives it with the ERP billing API as the Carrier invoice identity and relays the ERP's answer to the
// Carrier, with each line's result. Its workflow and local functions are deployed by azd's postdeploy hook
// (infra/deploy-carrier-invoice-logic-app.ps1), because the app needs the ERP billing API's address, which exists only after Aspire deploys it.

param location string
param tags object
param integrationRetentionDays int

@description('The Carrier invoice identity: the one principal holding the ERP billing API\'s receive role. The Logic App calls the ERP as it, and nothing else.')
param carrierInvoiceIdentityName string

@description('The ERP API app registration, the audience of the tokens the Logic App gets for the ERP billing API.')
param erpApiClientId string

@description('The ERP billing API\'s address, empty until the apps are first deployed. The postdeploy hook sets it.')
param erpBaseUrl string

param applicationInsightsName string

@description('The App Service plan of the intake Logic App, which this app shares.')
param planName string

@description('The Key Vault that holds the Logic Apps\' content connections.')
param contentKeyVaultName string

@description('API Management\'s identity, the one caller the trigger accepts.')
param apiManagementPrincipalId string
param apiManagementClientId string

@description('The app registration Easy Auth validates API Management\'s tokens for, and its audience.')
param carrierInvoiceTriggerClientId string
param carrierInvoiceTriggerAudience string

@description('The Integration console\'s managed identity, which reads the app\'s run history.')
param integrationConsolePrincipalId string

var resourceToken = uniqueString(resourceGroup().id)
var logicAppName = 'logic-carrier-invoice-${resourceToken}'
var contentShareName = logicAppName

resource carrierInvoiceIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: carrierInvoiceIdentityName
}

// The Logic App's own identity, for its storage and the Key Vault reference, so the Carrier invoice identity holds nothing but
// the ERP receive role.
resource hostIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-carrier-invoice-host-${resourceToken}'
  location: location
  tags: tags
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

resource plan 'Microsoft.Web/serverfarms@2024-11-01' existing = {
  name: planName
}

resource contentKeyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: contentKeyVaultName
}

// Logic Apps Standard keeps its state and run history here. It reaches blobs, queues and tables as its host identity.
resource storage 'Microsoft.Storage/storageAccounts@2024-01-01' = {
  name: 'stcarrinv${resourceToken}'
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
  name: 'carrier-invoice-logic-app-content'
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

resource logicApp 'Microsoft.Web/sites@2024-11-01' = {
  name: logicAppName
  location: location
  tags: tags
  kind: 'functionapp,workflowapp'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${hostIdentity.id}': {}
      '${carrierInvoiceIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: hostIdentity.id
    siteConfig: {
      // The Carrier invoice map and relay are .NET 8 local functions (ADR 0019).
      netFrameworkVersion: 'v8.0'
      use32BitWorkerProcess: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      // Needed when the app reaches its storage with a managed identity.
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
        // The workflow's parameters (parameters.json).
        { name: 'ERP_BASE_URL', value: erpBaseUrl }
        { name: 'ERP_API_CLIENT_ID', value: erpApiClientId }
        { name: 'CARRIER_INVOICE_IDENTITY_ID', value: carrierInvoiceIdentity.id }
        { name: 'API_MANAGEMENT_PRINCIPAL_ID', value: apiManagementPrincipalId }
      ]
    }
  }
  dependsOn: [
    storageAccess
    contentConnectionAccess
    contentShare
  ]
}

// Only API Management may call the trigger (ADR 0005, ADR 0020): Easy Auth accepts a token of its identity alone. Easy Auth lets
// other calls through to the runtime, which is what Logic Apps needs, and a SAS-signed callback URL still works; the
// workflow's first step refuses any caller Easy Auth didn't authenticate as API Management.
resource easyAuth 'Microsoft.Web/sites/config@2024-11-01' = {
  parent: logicApp
  name: 'authsettingsV2'
  properties: {
    platform: {
      enabled: true
    }
    globalValidation: {
      requireAuthentication: true
      // Logic Apps needs this: with Return401 the portal can't show the workflow.
      unauthenticatedClientAction: 'AllowAnonymous'
    }
    httpSettings: {
      requireHttps: true
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          openIdIssuer: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
          clientId: carrierInvoiceTriggerClientId
        }
        validation: {
          allowedAudiences: [
            carrierInvoiceTriggerAudience
            carrierInvoiceTriggerClientId
          ]
          defaultAuthorizationPolicy: {
            allowedApplications: [
              apiManagementClientId
            ]
            allowedPrincipals: {
              identities: [
                apiManagementPrincipalId
              ]
            }
          }
        }
      }
    }
  }
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
output logicAppHostName string = logicApp.properties.defaultHostName
