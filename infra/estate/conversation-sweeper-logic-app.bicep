// The conversation sweeper: a Logic Apps Standard app with one workflow that runs every night and deletes every Foundry conversation and
// response older than a day (docs/operations/foundry.md, "Conversation cleanup"). The Agent Service keeps conversations until they are deleted,
// so this is the safety net behind the delete after each run. Its workflow is deployed by azd's postdeploy hook
// (infra/deploy-conversation-sweeper-logic-app.ps1). It needs no secret: it calls Foundry as its managed identity, which holds the
// Foundry User role on the project and nothing else.
//
// It shares the intake Logic App's plan: a second Workflow Standard plan would double the cost of the sandbox for nothing.
// Its storage, host identity and run history are its own.

param location string
param tags object
param integrationRetentionDays int

param applicationInsightsName string

@description('The App Service plan of the intake Logic App, which this app shares.')
param planName string

@description('The Key Vault that holds the Logic Apps\' content connections.')
param contentKeyVaultName string

@description('The Integration console\'s managed identity, which reads the app\'s run history.')
param integrationConsolePrincipalId string

@description('The Foundry account and project (foundry.bicep) whose conversations the sweeper deletes, and the project\'s endpoint.')
param foundryAccountName string
param foundryProjectName string
param foundryProjectEndpoint string

@description('''How old a conversation or response must be before it is deleted, in minutes. A day is far shorter than integrationRetentionDays.
The smoke run (infra/smoke-test-conversation-sweeper.sh) sets it shorter for a moment.''')
@minValue(1)
param maxAgeMinutes int = 1440

@description('''False when the as-found toggle noConversationSweeper is set: the app is built but stopped, so nothing is ever swept and
conversation data piles up silently.''')
param enabled bool

var resourceToken = uniqueString(resourceGroup().id)
var logicAppName = 'logic-conversation-sweeper-${resourceToken}'
var contentShareName = logicAppName

// The identity the workflow's steps run as. It holds one role, Foundry User on the project, so it can list and delete what the
// Agents leave behind and nothing else: no model deployment, no management, no estate data.
resource sweeperIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-conversation-sweeper-${resourceToken}'
  location: location
  tags: tags
}

// The Logic App's own identity, for its storage and the Key Vault reference, so the sweeper identity holds nothing but
// what the workflow's steps need.
resource hostIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-conversation-sweeper-host-${resourceToken}'
  location: location
  tags: tags
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

// The Foundry User role (named Azure AI User in older documents): run agents and call models in the project, no management.
var foundryUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')

resource sweeperUsesTheProject 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryProject.id, sweeperIdentity.id, foundryUser)
  scope: foundryProject
  properties: {
    principalId: sweeperIdentity.properties.principalId
    roleDefinitionId: foundryUser
    principalType: 'ServicePrincipal'
  }
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
  name: 'stcvs${resourceToken}'
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
  name: 'conversation-sweeper-logic-app-content'
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
  tags: union(tags, { workload: 'conversation-sweeper' })
  kind: 'functionapp,workflowapp'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${hostIdentity.id}': {}
      '${sweeperIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    enabled: enabled
    keyVaultReferenceIdentity: hostIdentity.id
    siteConfig: {
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
        // The workflow's parameters (parameters.json). There is no secret among them: the workflow calls Foundry as its managed identity.
        { name: 'FOUNDRY_PROJECT_ENDPOINT', value: foundryProjectEndpoint }
        { name: 'CONVERSATION_SWEEPER_IDENTITY_ID', value: sweeperIdentity.id }
        { name: 'CONVERSATION_MAX_AGE_MINUTES', value: string(maxAgeMinutes) }
      ]
    }
  }
  dependsOn: [
    storageAccess
    contentConnectionAccess
    contentShare
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
