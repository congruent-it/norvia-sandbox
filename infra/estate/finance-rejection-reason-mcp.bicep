// The Carrier invoice rejection reason agent's door in Azure (ADR 0040, #473): an MCP server with the one tool, get_carrier_visible_line_facts,
// over the ERP API's /v1/agent/finance-rejection-reason, and an agent view app registration of its own with a role of its own. The reason
// leaves Norvia in the invoice outcome, so the boundary is the tool: that Agent's identity holds this role and nothing on the Finance agent
// view, so Entra issues it no token for the Carrier invoice matching agent's tools. The tool list comes from
// contracts/agent-mcp/finance-rejection-reason.tools.json, which the local MCP stand-in serves too.
//
//   rejection reason Agent --(token for the view, role CarrierInvoices.FinanceRejectionReasonAgent.Read)--> MCP server --(API Management's
//   token for the ERP agent API, the same role)--> the ERP API's /v1/agent/finance-rejection-reason
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity (its client ID), named in the tool call\'s authentication policy.')
param apiManagementClientId string

@description('The ERP agent API\'s client ID (finance-matching-mcp.bicep), the audience of the token API Management sends the ERP API.')
param erpAgentApiAppId string

@description('The ERP API\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_ERP_BASE_URL) and points the backend at it.')
param erpBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/finance-rejection-reason.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-finance-rejection-reason-view', environmentName, role)

// The rejection reason agent view: the audience of that Agent's token. Entra issues a token for it only to an identity that holds the role.
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
      displayName: 'Read what the Carrier can see of its line through the rejection reason agent\'s tool'
      description: 'Call the Carrier invoice rejection reason MCP server\'s one tool. Only the rejection reason Agent\'s identity holds it, never a person.'
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

resource erpBackend 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'erp-rejection-reason-agent-api'
  properties: {
    description: 'The ERP API\'s rejection reason agent view (/v1/agent/finance-rejection-reason). The tool policy names the route.'
    url: empty(erpBaseUrl) ? 'https://erp.invalid/v1/agent/finance-rejection-reason' : '${erpBaseUrl}/v1/agent/finance-rejection-reason'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Carrier invoice rejection reason agent\'s one read: what the Carrier can see of its own Carrier invoice line, never a Rate Card or its version, a Planner\'s remark, the matching tolerance, another Carrier\'s reference, a Shipper or Consignee\'s personal data or a file\'s contents.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    rateLimitCalls: 60
    backendId: erpBackend.name
    backendAudience: erpAgentApiAppId
    apiManagementIdentityClientId: apiManagementClientId
  }
}
