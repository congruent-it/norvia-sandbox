// The Estate health-check agent's door in Azure (ADR 0040): an API Management MCP server over the Estate health check's agent API,
// with the two app registrations that keep it narrow. Everything about the tools comes from the contract file the local MCP
// stand-in serves too (contracts/agent-mcp/health-check.tools.json), so the two cannot drift.
//
//   Agent --(token for the agent view, role HealthCheck.Agent.Read)--> MCP server --(API Management's token for the
//   app's agent API, the same role)--> the app's /agent/v1
//
// Only two kinds of identity hold a role here: each published Agent's identity on the view (infra/assign-agent-roles.sh gives it
// after every publish), and API Management's identity on the app's agent API (below). No operator holds either, and neither does the app's own identity, which holds Reader
// on the estate's resource group instead (health-check.bicep): the privileged role belongs to the app, never to the Agent (#371).
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity, which calls the app\'s agent API for the Agent (it holds the one role on that API).')
param apiManagementPrincipalId string

@description('API Management\'s identity (its client ID), named in the tool calls\' authentication policy.')
param apiManagementClientId string

@description('''The Estate health check\'s address, empty before the apps are first deployed. infra/deploy-agents.sh stores it (ESTATE_HEALTH_CHECK_BASE_URL)
and points the backend at it; the placeholder fails every tool call with a gateway error, which the Agent sees as a refused tool.''')
param healthCheckBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/health-check.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-health-check-view', environmentName, role)
var apiRoleId = guid('norvia-health-check-agent-api', environmentName, role)

// The agent view: the audience of an Agent's token. Entra issues a token for it only to an identity that holds the role.
resource view 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-agent-${contract.server}-view-${environmentName}'
  // infra/create-agent-connection.sh and infra/assign-agent-roles.sh look the view up by this name.
  displayName: 'Norvia ${contract.server} agent view (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-agent-${contract.server}-${environmentName}'
  ]
  api: {
    // v2.0 tokens carry the caller's client ID in azp, which the policy records as the Agent's identity.
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: viewRoleId
      value: role
      displayName: 'Read through the Estate health-check agent\'s tools'
      description: 'Call the Estate health-check MCP server\'s tools. Only the Agent\'s identity holds it, never an operator.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource viewPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: view.appId
  appRoleAssignmentRequired: true
}

// The app's agent API: the audience of the token API Management sends the app. The app takes this token on /agent/v1 and
// nothing else (the operators' sign-in is another app registration), and the role is the only one on it.
resource healthCheckAgentApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-health-check-agent-api-${environmentName}'
  // infra/provision-estate.sh finds it by this name.
  displayName: 'Norvia Estate health check agent API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-health-check-agent-api-${environmentName}'
  ]
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: apiRoleId
      value: role
      displayName: 'Read the Estate health check\'s agent API'
      description: 'Call /agent/v1 of the Estate health check. API Management\'s identity holds it, on behalf of the Agent.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource healthCheckAgentApiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: healthCheckAgentApi.appId
  appRoleAssignmentRequired: true
}

resource apiManagementReadsTheAgentApi 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: healthCheckAgentApiPrincipal.id
  appRoleId: apiRoleId
  principalId: apiManagementPrincipalId
}

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

// The app's agent API as a backend. The address comes after the app is deployed; the Bicep keeps what the deploy script stored.
resource healthCheckBackend 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'health-check-agent-api'
  properties: {
    description: 'The Estate health check\'s agent API (/agent/v1). The tool policies name the route.'
    url: empty(healthCheckBaseUrl) ? 'https://health-check.invalid/agent/v1' : '${healthCheckBaseUrl}/agent/v1'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Estate health-check agent\'s read-only view of the Estate health check: it runs the checklist and returns its Findings, with any secret masked, and lists the Logic Apps that failed and the dead-letter queues that hold messages, each with whether an alert watches it. It changes nothing.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    rateLimitCalls: 10
    backendId: healthCheckBackend.name
    backendAudience: healthCheckAgentApi.appId
    apiManagementIdentityClientId: apiManagementClientId
  }
  dependsOn: [
    apiManagementReadsTheAgentApi
  ]
}
