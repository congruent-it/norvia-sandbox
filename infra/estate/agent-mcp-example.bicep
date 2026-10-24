// The example MCP server: one read-only tool over nothing (API Management answers it itself), with the identities to call it.
// It is what the post-deploy smoke run (infra/smoke-test-agent-mcp-server.sh) calls and what shows an agent slice the shape of
// an agent-mcp-server call. It holds no data, so it stays deployed.
extension microsoftGraphV1

param environmentName string
param apiManagementName string

var contract = loadJsonContent('../../contracts/agent-mcp/example.tools.json')
var role = contract.role
var roleId = guid('norvia-agent-example', environmentName, role)

// The agent view's app registration: the audience of an Agent's token, with the one read role (identity and access matrix).
resource view 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-agent-example-view-${environmentName}'
  displayName: 'Norvia example agent view (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-agent-example-${environmentName}'
  ]
  api: {
    // v2.0 tokens carry the caller's client ID in azp, which the policy records as the Agent's identity.
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: roleId
      value: role
      displayName: 'Read the example agent view'
      description: 'Call the example MCP server\'s tools. Only the example identity holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource viewPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: view.appId
  // Entra issues tokens for the view only to identities that hold a role on it.
  appRoleAssignmentRequired: true
}

// The identity the smoke run calls as. It holds the read role.
resource caller 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-agent-example-${environmentName}'
  displayName: 'Norvia example agent identity (${environmentName})'
  signInAudience: 'AzureADMyOrg'
}

resource callerPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: caller.appId
}

resource callerReadsTheView 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: viewPrincipal.id
  appRoleId: roleId
  principalId: callerPrincipal.id
}

// An identity without the role, which the smoke run shows is refused. Entra itself refuses it a token for the view, so the
// smoke run calls the server with a token for another audience as well.
resource strangerApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-agent-example-stranger-${environmentName}'
  displayName: 'Norvia example identity without the role (${environmentName})'
  signInAudience: 'AzureADMyOrg'
}

resource strangerPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: strangerApp.appId
}

module server 'agent-mcp-server.bicep' = {
  params: {
    apiManagementName: apiManagementName
    serverName: contract.server
    purpose: 'The example MCP server: one read-only tool that API Management answers itself. It proves the agent door end to end.'
    tools: contract.tools
    audience: view.appId
    requiredRole: role
    rateLimitCalls: 60
  }
  dependsOn: [
    callerReadsTheView
  ]
}

output mcpServerUrl string = server.outputs.mcpServerUrl
output audience string = view.appId
output callerClientId string = caller.appId
output strangerClientId string = strangerApp.appId
