// An application's agent view behind an API Management MCP server (ADR 0040), so each agent epic supplies only its tool list.
// The module makes the agent view an API (one operation per tool), the MCP server over it (one tool per operation) and the
// server's policy. The tool list is the contract file contracts/agent-mcp/<server>.tools.json, which the local MCP stand-in
// reads too (ADR 0041); docs/operations/agent-mcp-servers.md says how to add one.

@description('The estate\'s API Management service, which the MCP server is added to.')
param apiManagementName string

@description('The server\'s name: lower-case letters, digits and hyphens. The MCP server is served at /agents/<serverName>/mcp.')
@minLength(2)
@maxLength(40)
param serverName string

@description('What the server is for, shown in API Management.')
param purpose string

@description('''The tools, in the shape of the contract file: name, description, method (GET only: an Agent has no write tools) and path.
A tool with a mockResponse is answered by API Management itself, which is only for the example server.''')
param tools array

@description('The agent view\'s app registration (its client ID): the audience of the Agent\'s token.')
param audience string

@description('The one read role the Agent\'s token must carry.')
param requiredRole string

@description('The calls one Agent identity may make a minute.')
@minValue(1)
param rateLimitCalls int = 60

@description('The window of the rate limit, in seconds.')
@minValue(1)
param rateLimitPeriodSeconds int = 60

@description('The Entra tenant whose tokens the policy validates.')
param tenantId string = tenant().tenantId

@description('The name of the API Management backend that is the application\'s agent view, empty when every tool has a mockResponse.')
param backendId string = ''

@description('The application\'s API app registration, which the tool calls get a token for as API Management\'s identity. Empty when every tool has a mockResponse.')
param backendAudience string = ''

@description('API Management\'s identity (its client ID), which makes the tool calls. Empty when every tool has a mockResponse.')
param apiManagementIdentityClientId string = ''


// The policies are templates: each __TOKEN__ is replaced by the module's value, XML-escaped.
func xmlEscaped(text string) string => replace(replace(replace(text, '&', '&amp;'), '<', '&lt;'), '>', '&gt;')
func fill(template string, values array) string => reduce(values, template, (policy, entry) => replace(policy, entry.token, xmlEscaped(entry.value)))

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

// The agent view. It is an API on the gateway too, so each operation checks the token itself, and it is in no product.
resource agentView 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: apiManagement
  name: '${serverName}-agent-view'
  properties: {
    displayName: 'Agent view: ${serverName}'
    description: purpose
    path: 'agent-views/${serverName}'
    protocols: [
      'https'
    ]
    subscriptionRequired: false
  }
}

resource viewOperations 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = [
  for tool in tools: {
    parent: agentView
    name: tool.name
    properties: {
      displayName: tool.name
      description: tool.description
      method: tool.method
      urlTemplate: tool.path
      // API Management wants every {placeholder} of the URL template declared: "/runs/{a}/{b}" splits on '{' into
      // "/runs/", "a}/" and "b}", and each part after the first starts with a name that ends at '}'.
      templateParameters: map(skip(split(tool.path, '{'), 1), part => {
        name: split(part, '}')[0]
        required: true
        type: 'string'
      })
    }
  }
]

var viewPolicy = loadTextContent('agent-mcp-server/view-operation.policy.xml')
var mockPolicy = loadTextContent('agent-mcp-server/view-operation-mock.policy.xml')

resource viewOperationPolicies 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = [
  for (tool, i) in tools: {
    parent: viewOperations[i]
    name: 'policy'
    properties: {
      format: 'rawxml'
      value: fill(contains(tool, 'mockResponse') ? mockPolicy : viewPolicy, [
        { token: '__TENANT_ID__', value: tenantId }
        { token: '__AUDIENCE__', value: audience }
        { token: '__ROLE__', value: requiredRole }
        { token: '__BACKEND_ID__', value: backendId }
        { token: '__BACKEND_AUDIENCE__', value: backendAudience }
        { token: '__IDENTITY_CLIENT_ID__', value: apiManagementIdentityClientId }
        { token: '__PATH__', value: tool.path }
        { token: '__MOCK_RESPONSE__', value: contains(tool, 'mockResponse') ? string(tool.mockResponse) : '' }
      ])
    }
  }
]

// The MCP server over the agent view. Each tool is one operation of it.
resource mcpServer 'Microsoft.ApiManagement/service/apis@2025-09-01-preview' = {
  parent: apiManagement
  name: '${serverName}-mcp'
  properties: {
    type: 'mcp'
    displayName: 'MCP server: ${serverName}'
    description: purpose
    path: 'agents/${serverName}'
    protocols: [
      'https'
    ]
    // The Agent's Entra token is the credential. There is no subscription key to hand an Agent.
    subscriptionRequired: false
  }
  dependsOn: [
    viewOperationPolicies
  ]
}

resource mcpTools 'Microsoft.ApiManagement/service/apis/tools@2025-09-01-preview' = [
  for (tool, i) in tools: {
    parent: mcpServer
    name: tool.name
    properties: {
      displayName: tool.name
      description: tool.description
      operationId: viewOperations[i].id
    }
  }
]

var serverPolicy = loadTextContent('agent-mcp-server/mcp-server.policy.xml')

resource mcpServerPolicy 'Microsoft.ApiManagement/service/apis/policies@2025-09-01-preview' = {
  parent: mcpServer
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: fill(serverPolicy, [
      { token: '__TENANT_ID__', value: tenantId }
      { token: '__AUDIENCE__', value: audience }
      { token: '__ROLE__', value: requiredRole }
      { token: '__SERVER__', value: serverName }
      { token: '__RATE_LIMIT_CALLS__', value: string(rateLimitCalls) }
      { token: '__RATE_LIMIT_PERIOD__', value: string(rateLimitPeriodSeconds) }
      { token: '__TOOLS__', value: join(map(tools, tool => tool.name), '|') }
    ])
  }
  dependsOn: [
    mcpTools
  ]
}

@description('The MCP server\'s address.')
output mcpServerUrl string = '${apiManagement.properties.gatewayUrl}/agents/${serverName}/mcp'
