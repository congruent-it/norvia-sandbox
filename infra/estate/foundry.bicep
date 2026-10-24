// The Foundry the Agents run on (ADR 0040): one account and one project with a public endpoint behind Entra, one small shared
// model deployment that keeps processing in the EU Data Boundary, and Foundry's traces in the estate's Application Insights.
// No agent is defined here: a deploy script publishes them (#499).

param location string
param tags object

@description('The estate\'s Application Insights, which Foundry\'s server-side traces land in, next to the Message trails.')
param applicationInsightsName string

@description('''The model every Agent shares, from the "model region availability by deployment type" list for DataZoneStandard in the EU
(docs/operations/foundry.md, spike 1).''')
param modelName string

@description('The model\'s version.')
param modelVersion string

@description('''Whether the model deployment exists. Switch it off and the account and project stay but every Agent run fails, which is
how an agent toggle breaks the Agents (#377).''')
param modelDeployed bool

@description('''The deployment\'s quota in thousands of tokens per minute: the cap on what the Agents can spend. DataZoneStandard
deployments are billed by token, so the cap is the ceiling.''')
@minValue(1)
@maxValue(100)
param modelCapacity int

@description('Whether the Agents do anything (Agents:Enabled). It is recorded on the project so the setting is visible in Azure.')
param agentsEnabled bool

@description('Entra object IDs that may use the project: the presenter, and the pipeline that deploys the Agents. Each gets the Foundry User role.')
param foundryUserPrincipalIds array

var resourceToken = uniqueString(resourceGroup().id)

// What the cost story reads: every Foundry resource carries it.
var foundryTags = union(tags, {
  workload: 'foundry-agents'
  'cost-category': 'ai-model'
  'agents-enabled': agentsEnabled ? 'true' : 'false'
})

// The Foundry User role (named Azure AI User in older documents): run agents and call models in the project, no management.
var foundryUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

// A Foundry resource: kind AIServices with project management on. Local (key) auth is off, so only Entra gets in.
resource account 'Microsoft.CognitiveServices/accounts@2025-06-01' = {
  name: 'ais-norvia-${resourceToken}'
  location: location
  tags: foundryTags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    allowProjectManagement: true
    customSubDomainName: 'ais-norvia-${resourceToken}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' = {
  parent: account
  name: 'norvia'
  location: location
  tags: foundryTags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: 'Norvia agents'
    description: 'The Norvia Agents (ADR 0040).'
  }
}

// DataZoneStandard: prompts and responses are processed in the EU data zone and nowhere else. GlobalStandard may process anywhere,
// so it is not used. The capacity is the cap on spend.
resource model 'Microsoft.CognitiveServices/accounts/deployments@2025-06-01' = if (modelDeployed) {
  parent: account
  name: modelName
  tags: foundryTags
  sku: {
    name: 'DataZoneStandard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
    versionUpgradeOption: 'NoAutoUpgrade'
  }
}

// Connecting Application Insights turns on server-side tracing for every agent in the project. Whether that can leave out the
// prompt content is spike 2 (docs/operations/foundry.md): the documents give no such switch, so the content is kept apart in the protected AppGenAIContent table.
resource tracing 'Microsoft.CognitiveServices/accounts/connections@2025-04-01-preview' = {
  parent: account
  name: 'application-insights'
  properties: {
    category: 'AppInsights'
    target: applicationInsights.id
    authType: 'ApiKey'
    isSharedToAll: true
    credentials: {
      key: applicationInsights.properties.ConnectionString
    }
    metadata: {
      ApiType: 'Azure'
      ResourceId: applicationInsights.id
    }
  }
}

resource usersUseTheProject 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in foundryUserPrincipalIds: {
  name: guid(project.id, principalId, foundryUser)
  scope: project
  properties: {
    principalId: principalId
    roleDefinitionId: foundryUser
  }
}]

output accountName string = account.name
output projectName string = project.name
@description('The project endpoint the apps and scripts call (FOUNDRY_PROJECT_ENDPOINT).')
output projectEndpoint string = 'https://${account.properties.customSubDomainName}.services.ai.azure.com/api/projects/${project.name}'
output modelDeploymentName string = modelName
