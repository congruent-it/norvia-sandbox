// The Carrier invoice matching agent's door in Azure (ADR 0040): an API Management API in front of the ERP API's Finance agent view and an
// MCP server over it, with the two app registrations that keep it narrow. The ERP API has no other API Management front for an agent. Everything
// about the tools comes from the contract file the local MCP stand-in serves too (contracts/agent-mcp/finance-matching.tools.json), so the two
// cannot drift.
//
//   Agent --(token for the agent view, role CarrierInvoices.FinanceAgent.Read)--> MCP server --(API Management's token for
//   the ERP agent API, the same role)--> the ERP API's /v1/agent/finance
//
// Only two kinds of identity hold a role here: each published Agent's identity on the view (infra/assign-agent-roles.ps1 gives it after every
// publish), and API Management's identity on the ERP agent API (below). No person holds either, and neither does any caller of the ERP API's
// other endpoints: the ERP API takes the agent API's tokens on the Finance agent view alone, from a scheme of its own.
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity, which calls the ERP Finance agent view for the Agent (it holds the one role on that API).')
param apiManagementPrincipalId string

@description('API Management\'s identity (its client ID), named in the tool calls\' authentication policy.')
param apiManagementClientId string

@description('''The ERP API\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_ERP_BASE_URL) and points the
backend at it; the placeholder fails every tool call with a gateway error, which the Agent sees as a refused tool.''')
param erpBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/finance-matching.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-finance-matching-view', environmentName, role)
var apiRoleId = guid('norvia-erp-agent-api', environmentName, role)
// The Carrier invoice rejection reason agent's role (finance-rejection-reason-mcp.bicep) is the second role of the ERP agent API: it has a
// view of its own in the ERP API (/v1/agent/finance-rejection-reason) and a role requirement of its own there, so the one agent API app
// registration serves both views while the two Agents' identities hold different roles on different agent views.
var rejectionReasonRole = loadJsonContent('../../contracts/agent-mcp/finance-rejection-reason.tools.json').role
var rejectionReasonApiRoleId = guid('norvia-erp-agent-api', environmentName, rejectionReasonRole)

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
      displayName: 'Read through the Carrier invoice matching agent\'s tools'
      description: 'Call the Carrier invoice matching MCP server\'s tools. Only the Agent\'s identity holds it, never a person.'
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

// The ERP agent API: the audience of the token API Management sends the ERP API. The ERP API takes this token on the Finance agent view and
// nothing else (the callers of its other endpoints use the ERP API's own app registration), and the role is the only one on it.
resource erpAgentApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-erp-agent-api-${environmentName}'
  // infra/provision-estate.ps1 finds it by this name.
  displayName: 'Norvia ERP agent API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-erp-agent-api-${environmentName}'
  ]
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: apiRoleId
      value: role
      displayName: 'Read the ERP API\'s Finance agent view'
      description: 'Call /v1/agent/finance of the ERP API. API Management\'s identity holds it, on behalf of the Agent.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: rejectionReasonApiRoleId
      value: rejectionReasonRole
      displayName: 'Read the ERP API\'s rejection reason agent view'
      description: 'Call /v1/agent/finance-rejection-reason of the ERP API, what the Carrier can see of its own line. API Management\'s identity holds it, on behalf of the rejection reason Agent.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource erpAgentApiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: erpAgentApi.appId
  appRoleAssignmentRequired: true
}

resource apiManagementReadsTheAgentApi 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: erpAgentApiPrincipal.id
  appRoleId: apiRoleId
  principalId: apiManagementPrincipalId
}

resource apiManagementReadsTheRejectionReasonAgentApi 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: erpAgentApiPrincipal.id
  appRoleId: rejectionReasonApiRoleId
  principalId: apiManagementPrincipalId
}

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

// The ERP Finance agent view as a backend. The address comes after the ERP API is deployed; the Bicep keeps what the deploy script stored.
resource erpBackend 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'erp-agent-api'
  properties: {
    description: 'The ERP API\'s Finance agent view (/v1/agent/finance). The tool policies name the route.'
    url: empty(erpBaseUrl) ? 'https://erp.invalid/v1/agent/finance' : '${erpBaseUrl}/v1/agent/finance'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Carrier invoice matching agent\'s read-only view of the ERP: one Carrier invoice line or a whole Carrier invoice, with its result, the invoiced amount, the agreed Carrier cost total and the difference, the matching tolerance, the cost\'s source as a label, the earlier line of a Duplicate and the likely candidate Carrier costs of an Unknown booking, and for a whole invoice its lines counted by result, never a Planner\'s remark, never a Shipper or Consignee\'s personal data and never a file\'s contents.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    // Every MCP message counts, not only tool calls: one Foundry run sends initialize, initialized, a GET and tools/list before its first tool call.
    rateLimitCalls: 60
    backendId: erpBackend.name
    backendAudience: erpAgentApi.appId
    apiManagementIdentityClientId: apiManagementClientId
  }
  dependsOn: [
    apiManagementReadsTheAgentApi
  ]
}

@description('The ERP agent API\'s client ID, the audience of the token API Management sends the ERP API, for the rejection reason Agent\'s server too.')
output erpAgentApiAppId string = erpAgentApi.appId
