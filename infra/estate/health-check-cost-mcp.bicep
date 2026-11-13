// The FinOps agent's door in Azure (#511, ADR 0040): an API Management MCP server over the cost tools of the Estate health check's agent
// API, with an agent view of its own. Everything about the tools comes from the contract file the local MCP stand-in serves too
// (contracts/agent-mcp/health-check-cost.tools.json), so the two cannot drift.
//
//   FinOps agent --(token for its own view, role HealthCheck.Cost.Read)--> MCP server --(API Management's token for the app's
//   agent API, the same role)--> the app's /agent/v1/cost and /agent/v1/rising-spend
//
// The agent API's app registration, its backend and API Management's role on it are health-check-mcp.bicep's: this module reads them and
// adds the Agent's view only. The Agent holds one role, on its view, and no Azure role: reading Cost Management is the app's own identity's
// job (health-check.bicep), never the Agent's (#371).
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity (its client ID), named in the tool calls\' authentication policy.')
param apiManagementClientId string

var contract = loadJsonContent('../../contracts/agent-mcp/health-check-cost.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-health-check-cost-view', environmentName, role)

// The agent view: the audience of the FinOps agent's token. Entra issues a token for it only to an identity that holds the role, which
// the health-check agent's identity does not.
resource view 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-agent-${contract.server}-view-${environmentName}'
  // infra/assign-agent-roles.ps1 looks the view up by this name.
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
      displayName: 'Read through the FinOps agent\'s tools'
      description: 'Call the FinOps MCP server\'s tools. Only the Agent\'s identity holds it, never an operator.'
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

// The app's agent API, made by health-check-mcp.bicep.
resource healthCheckAgentApi 'Microsoft.Graph/applications@v1.0' existing = {
  uniqueName: 'norvia-health-check-agent-api-${environmentName}'
}

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

// The backend health-check-mcp.bicep keeps pointed at the app, whose address the deploy script stores.
resource healthCheckBackend 'Microsoft.ApiManagement/service/backends@2024-05-01' existing = {
  parent: apiManagement
  name: 'health-check-agent-api'
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The FinOps agent\'s read-only view of the estate\'s spend: spend per resource next to Transport Order volume with each change worked out by code, and the Findings of the rising-spend Check. It changes nothing and reads no Azure resource itself.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    // Every MCP message counts, not only tool calls: one Foundry run sends initialize, initialized, a GET and tools/list before its first tool call.
    rateLimitCalls: 60
    backendId: healthCheckBackend.name
    backendAudience: healthCheckAgentApi.appId
    apiManagementIdentityClientId: apiManagementClientId
  }
}
