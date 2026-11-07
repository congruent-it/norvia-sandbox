// The Customer service reply agent's door in Azure (ADR 0011, ADR 0040): an MCP server with the one tool, get_shipper_view, over the TMS
// API's /v1/agent/customer-service-reply, and an agent view app registration of its own with a role of its own. The reply Agent's
// identity holds that role and nothing on the Customer service agent's view, so Entra issues it no token for the other tools. The
// tool list comes from contracts/agent-mcp/customer-service-reply.tools.json, which the local MCP stand-in serves too.
//
//   reply Agent --(token for the reply view, role TransportOrders.CustomerServiceReplyAgent.Read)--> MCP server --(API Management's token
//   for the TMS agent API, the same role)--> the TMS API's /v1/agent/customer-service-reply
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity (its client ID), named in the tool call\'s authentication policy.')
param apiManagementClientId string

@description('The TMS agent API\'s client ID (customer-service-mcp.bicep), the audience of the token API Management sends the TMS API.')
param tmsAgentApiAppId string

@description('The TMS API\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_TMS_BASE_URL) and points the backend at it.')
param tmsBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/customer-service-reply.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-customer-service-reply-view', environmentName, role)

// The reply agent view: the audience of the reply Agent's token. Entra issues a token for it only to an identity that holds the role.
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
      displayName: 'Read the Shipper view through the Customer service reply agent\'s tool'
      description: 'Call the Customer service reply MCP server\'s one tool. Only the reply Agent\'s identity holds it, never a person.'
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
  name: 'tms-reply-agent-api'
  properties: {
    description: 'The TMS API\'s Customer service reply agent view (/v1/agent/customer-service-reply). The tool policy names the route.'
    url: empty(tmsBaseUrl) ? 'https://tms.invalid/v1/agent/customer-service-reply' : '${tmsBaseUrl}/v1/agent/customer-service-reply'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Customer service reply agent\'s one read: what the Shipper sees of a Transport Order, never a Carrier, an Exception, a remark, a Consignee or a price.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    rateLimitCalls: 60
    backendId: tmsBackend.name
    backendAudience: tmsAgentApiAppId
    apiManagementIdentityClientId: apiManagementClientId
  }
}
