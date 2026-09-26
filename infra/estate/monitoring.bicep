// Where the estate's telemetry goes: the TMS's traces, the Container Apps logs and the Logic Apps' runs.
// The Integration console reads it from here (ADR 0017).

param location string
param tags object
param integrationRetentionDays int

@description('The Integration console\'s managed identity, which searches the workspace for Message trails.')
param integrationConsolePrincipalId string

var resourceToken = uniqueString(resourceGroup().id)

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: 'log-norvia-${resourceToken}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: integrationRetentionDays
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-norvia-${resourceToken}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    RetentionInDays: integrationRetentionDays
  }
}

// The console searches Message trails here as its own identity, never with the operator's token (ADR 0017). Reading is
// all it needs: it keeps nothing of its own.
var logAnalyticsReader = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '73c42c96-874c-492b-b04d-ab87d138a893')

resource integrationConsoleReadsTheWorkspace 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(workspace.id, integrationConsolePrincipalId, logAnalyticsReader)
  scope: workspace
  properties: {
    principalId: integrationConsolePrincipalId
    roleDefinitionId: logAnalyticsReader
    principalType: 'ServicePrincipal'
  }
}

output workspaceName string = workspace.name
@description('The workspace\'s ID (its customer ID), which queries address it by.')
output workspaceId string = workspace.properties.customerId
output applicationInsightsName string = applicationInsights.name
