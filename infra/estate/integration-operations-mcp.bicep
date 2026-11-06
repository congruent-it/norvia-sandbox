// The Integration operations agent's door in Azure (ADR 0040): an API Management MCP server over the Integration console's agent
// API, with the two app registrations that keep it narrow. Everything about the tools comes from the contract file the local MCP
// stand-in serves too (contracts/agent-mcp/integration-operations.tools.json), so the two cannot drift.
//
//   Agent --(token for the agent view, role IntegrationConsole.Agent.Read)--> MCP server --(API Management's token for the
//   console's agent API, the same role)--> the console's /agent/v1
//
// Only two kinds of identity hold a role here: each published Agent's identity on the view (infra/assign-agent-roles.ps1 gives it
// after every publish), and API Management's identity on the console's agent API (below). No operator holds either.
extension microsoftGraphV1

param environmentName string
param apiManagementName string

@description('API Management\'s identity, which calls the console\'s agent API for the Agent (it holds the one role on that API).')
param apiManagementPrincipalId string

@description('API Management\'s identity (its client ID), named in the tool calls\' authentication policy.')
param apiManagementClientId string

@description('''The Integration console\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_INTEGRATION_CONSOLE_BASE_URL)
and points the backend at it; the placeholder fails every tool call with a gateway error, which the Agent sees as a refused tool.''')
param consoleBaseUrl string = ''

var contract = loadJsonContent('../../contracts/agent-mcp/integration-operations.tools.json')
var role = contract.role
var viewRoleId = guid('norvia-agent-integration-operations-view', environmentName, role)
var apiRoleId = guid('norvia-integration-console-agent-api', environmentName, role)

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
      displayName: 'Read through the Integration operations agent\'s tools'
      description: 'Call the Integration operations MCP server\'s tools. Only the Agent\'s identity holds it, never an operator.'
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

// The console's agent API: the audience of the token API Management sends the console. The console takes this token on /agent/v1 and
// nothing else (the operators' sign-in is another app registration), and the role is the only one on it.
resource consoleAgentApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-integration-console-agent-api-${environmentName}'
  displayName: 'Norvia Integration console agent API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-integration-console-agent-api-${environmentName}'
  ]
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: apiRoleId
      value: role
      displayName: 'Read the Integration console\'s agent API'
      description: 'Call /agent/v1 of the Integration console. API Management\'s identity holds it, on behalf of the Agent.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource consoleAgentApiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: consoleAgentApi.appId
  appRoleAssignmentRequired: true
}

resource apiManagementReadsTheAgentApi 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: consoleAgentApiPrincipal.id
  appRoleId: apiRoleId
  principalId: apiManagementPrincipalId
}

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

// The console's agent API as a backend. The address comes after the console is deployed; the Bicep keeps what the deploy script stored.
resource consoleBackend 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'integration-console-agent-api'
  properties: {
    description: 'The Integration console\'s agent API (/agent/v1). The tool policies name the route.'
    url: empty(consoleBaseUrl) ? 'https://integration-console.invalid/agent/v1' : '${consoleBaseUrl}/agent/v1'
    protocol: 'http'
  }
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The Integration operations agent\'s read-only view of the Integration console: Message trails, Logic App runs, failed runs and dead-letter queues (counts, and a peek that changes nothing), never a message body.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    rateLimitCalls: 30
    backendId: consoleBackend.name
    backendAudience: consoleAgentApi.appId
    apiManagementIdentityClientId: apiManagementClientId
  }
  dependsOn: [
    apiManagementReadsTheAgentApi
  ]
}
