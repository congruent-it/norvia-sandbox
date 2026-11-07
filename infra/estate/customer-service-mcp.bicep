// The Customer service agent's door in Azure (ADR 0040): an API Management API in front of the TMS API's agent view and an MCP server
// over it, with the two app registrations that keep it narrow. The TMS API has no other API Management front: the Shipper tracking API
// is the Partner API's. Everything about the tools comes from the contract file the local MCP stand-in serves too
// (contracts/agent-mcp/customer-service.tools.json), so the two cannot drift.
//
//   Agent --(token for the agent view, role TransportOrders.CustomerServiceAgent.Read)--> MCP server --(API Management's token for
//   the TMS agent API, the same role)--> the TMS API's /v1/agent/customer-service
//
// Only two kinds of identity hold a role here: each published Agent's identity on the view (infra/assign-agent-roles.ps1 gives it
// after every publish), and API Management's identity on the TMS agent API (below). No person holds either, and neither does any
// caller of the TMS API's other endpoints: the TMS API takes the agent API's tokens on the agent view alone, from a scheme of its own.
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity, which calls the TMS agent view for the Agent (it holds the one role on that API).')
param apiManagementPrincipalId string

@description('API Management\'s identity (its client ID), named in the tool calls\' authentication policy.')
param apiManagementClientId string

@description('''The TMS API\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_TMS_BASE_URL) and points the
backend at it; the placeholder fails every tool call with a gateway error, which the Agent sees as a refused tool.''')
param tmsBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/customer-service.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-customer-service-view', environmentName, role)
var apiRoleId = guid('norvia-tms-agent-api', environmentName, role)
// The Customer service reply agent's role (customer-service-reply-mcp.bicep) is the second role of the TMS agent API: it has a view of its
// own in the TMS API (/v1/agent/customer-service-reply) and a role requirement of its own there, so the one agent API app registration
// serves both views while the two Agents' identities hold different roles on different agent views.
var replyRole = loadJsonContent('../../contracts/agent-mcp/customer-service-reply.tools.json').role
var replyApiRoleId = guid('norvia-tms-agent-api', environmentName, replyRole)

// The agent view: the audience of an Agent's token. Entra issues a token for it only to an identity that holds the role.
resource view 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-agent-${contract.server}-view-${environmentName}'
  // infra/create-agent-connection.ps1 and infra/assign-agent-roles.ps1 look the view up by this name.
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
      displayName: 'Read through the Customer service agent\'s tools'
      description: 'Call the Customer service MCP server\'s tools. Only the Agent\'s identity holds it, never a person.'
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

// The TMS agent API: the audience of the token API Management sends the TMS API. The TMS API takes this token on the agent view and
// nothing else (the callers of its other endpoints use the TMS API's own app registration), and the role is the only one on it.
resource tmsAgentApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-tms-agent-api-${environmentName}'
  // infra/provision-estate.ps1 finds it by this name.
  displayName: 'Norvia TMS agent API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-tms-agent-api-${environmentName}'
  ]
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: apiRoleId
      value: role
      displayName: 'Read the TMS API\'s Customer service agent view'
      description: 'Call /v1/agent/customer-service of the TMS API. API Management\'s identity holds it, on behalf of the Agent.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: replyApiRoleId
      value: replyRole
      displayName: 'Read the TMS API\'s Customer service reply agent view'
      description: 'Call /v1/agent/customer-service-reply of the TMS API, the Shipper view. API Management\'s identity holds it, on behalf of the reply Agent.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource tmsAgentApiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: tmsAgentApi.appId
  appRoleAssignmentRequired: true
}

resource apiManagementReadsTheAgentApi 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsAgentApiPrincipal.id
  appRoleId: apiRoleId
  principalId: apiManagementPrincipalId
}

resource apiManagementReadsTheReplyAgentApi 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsAgentApiPrincipal.id
  appRoleId: replyApiRoleId
  principalId: apiManagementPrincipalId
}

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

// The TMS agent view as a backend. The address comes after the TMS API is deployed; the Bicep keeps what the deploy script stored.
resource tmsBackend 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'tms-agent-api'
  properties: {
    description: 'The TMS API\'s Customer service agent view (/v1/agent/customer-service). The tool policies name the route.'
    url: empty(tmsBaseUrl) ? 'https://tms.invalid/v1/agent/customer-service' : '${tmsBaseUrl}/v1/agent/customer-service'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Customer service agent\'s read-only view of the TMS: a Rejected booking\'s reasons, which fields failed and what the Shipper submitted, and a Transport Order\'s rating, Shipment, tracking, Exceptions, Proof of delivery status, billing state and whether an order may be released without a Proof of delivery, never a Consignee\'s name, address or contact and never a file\'s contents.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    // Every MCP message counts, not only tool calls: one Foundry run sends initialize, initialized, a GET and tools/list before its first tool call.
    rateLimitCalls: 60
    backendId: tmsBackend.name
    backendAudience: tmsAgentApi.appId
    apiManagementIdentityClientId: apiManagementClientId
  }
  dependsOn: [
    apiManagementReadsTheAgentApi
  ]
}

@description('The TMS agent API\'s client ID, the audience of the token API Management sends the TMS API, for the reply Agent\'s server too.')
output tmsAgentApiAppId string = tmsAgentApi.appId

