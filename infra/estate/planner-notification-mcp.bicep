// The Planner notification agent's door in Azure (ADR 0011, ADR 0040): an MCP server with the two tools get_shipper_notification_template
// and get_shipper_view, over the TMS API's /v1/agent/planner-notification, and an agent view app registration of its own with a role of
// its own. The Agent's identity holds that role and nothing on any other Agent's view, so Entra issues it no token for the other tools. The
// tool list comes from contracts/agent-mcp/planner-notification.tools.json, which the local MCP stand-in serves too.
//
//   Planner notification Agent --(token for its view, role TransportOrders.PlannerNotificationAgent.Read)--> MCP server --(API Management's token
//   for the TMS agent API, the same role)--> the TMS API's /v1/agent/planner-notification
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity (its client ID), named in the tool call\'s authentication policy.')
param apiManagementClientId string

@description('The TMS agent API\'s client ID (customer-service-mcp.bicep), the audience of the token API Management sends the TMS API.')
param tmsAgentApiAppId string

@description('The TMS API\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_TMS_BASE_URL) and points the backend at it.')
param tmsBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/planner-notification.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-planner-notification-view', environmentName, role)

// The Planner notification agent view: the audience of the Agent's token. Entra issues a token for it only to an identity that holds the role.
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
      displayName: 'Read the Notify Shipper template and the Shipper view through the Planner notification agent\'s tools'
      description: 'Call the Planner notification MCP server\'s two tools. Only the Planner notification Agent\'s identity holds it, never a person.'
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
  name: 'tms-planner-notification-agent-api'
  properties: {
    description: 'The TMS API\'s Planner notification agent view (/v1/agent/planner-notification). The tool policy names the route.'
    url: empty(tmsBaseUrl) ? 'https://tms.invalid/v1/agent/planner-notification' : '${tmsBaseUrl}/v1/agent/planner-notification'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Planner notification agent\'s two reads: the Notify Shipper template of an Exception and what the Shipper sees of a Transport Order, never a Carrier, a remark, an Exception, a price or a file.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    rateLimitCalls: 60
    backendId: tmsBackend.name
    backendAudience: tmsAgentApiAppId
    apiManagementIdentityClientId: apiManagementClientId
  }
}
