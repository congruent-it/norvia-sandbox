// The Planner agent's door in Azure (ADR 0040): an MCP server with the one tool, get_assignment_options, over the TMS
// API's /v1/agent/planner, and an agent view app registration of its own with a role of its own. The Planner Agent's
// identity holds that role and nothing on any other Agent's view, so Entra issues it no token for the other tools. The
// tool list comes from contracts/agent-mcp/planner.tools.json, which the local MCP stand-in serves too.
//
//   Planner Agent --(token for the Planner view, role TransportOrders.PlannerAgent.Read)--> MCP server --(API Management's token
//   for the TMS agent API, the same role)--> the TMS API's /v1/agent/planner
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity (its client ID), named in the tool call\'s authentication policy.')
param apiManagementClientId string

@description('The TMS agent API\'s client ID (customer-service-mcp.bicep), the audience of the token API Management sends the TMS API.')
param tmsAgentApiAppId string

@description('The TMS API\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_TMS_BASE_URL) and points the backend at it.')
param tmsBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/planner.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-planner-view', environmentName, role)

// The Planner agent view: the audience of the Planner Agent's token. Entra issues a token for it only to an identity that holds the role.
resource view 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-agent-${contract.server}-view-${environmentName}'
  // infra/assign-agent-roles.ps1 looks the view up by this name.
  displayName: 'Norvia ${contract.server} agent view (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-agent-${contract.server}-${environmentName}'
  ]
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: viewRoleId
      value: role
      displayName: 'Read the assignment options through the Planner agent\'s tool'
      description: 'Call the Planner MCP server\'s one tool. Only the Planner Agent\'s identity holds it, never a person.'
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

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

resource tmsBackend 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'tms-planner-agent-api'
  properties: {
    description: 'The TMS API\'s Planner agent view (/v1/agent/planner). The tool policy names the route.'
    url: empty(tmsBaseUrl) ? 'https://tms.invalid/v1/agent/planner' : '${tmsBaseUrl}/v1/agent/planner'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Planner agent\'s one read: where a Transport Order waiting for planning can go, never a street address, a Consignee, an instruction or a file.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    rateLimitCalls: 60
    backendId: tmsBackend.name
    backendAudience: tmsAgentApiAppId
    apiManagementIdentityClientId: apiManagementClientId
  }
}
