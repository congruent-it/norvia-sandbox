// Where the estate's telemetry goes: the TMS's traces, the Container Apps logs and, from P1.9, the Logic Apps' runs.
// The Integration console reads it from here (ADR 0017).

param location string
param tags object
param integrationRetentionDays int

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

output workspaceName string = workspace.name
output applicationInsightsName string = applicationInsights.name
