// The Estate health check app in Azure (#392, ADR 0026 as for the Integration console): the identity it runs as, with Reader and
// Cost Management Reader on the estate's resource group, and the app registration Easy Auth signs Integration operators in to. The privileged role belongs to the
// app, never to the Agent (#371, question 1): the Agent's identity holds the one call role on the agent view
// (health-check-mcp.bicep) and no Azure role. Aspire deploys the app; the estate owns its identity because the estate grants it
// what it may read.
//
// Like the other agent modules it has no outputs of the template (the estate is at the 64 a template may have), bar the identity's
// principal for the Foundry User role; infra/provision-estate.ps1 finds the registration and the identity by their names.
extension microsoftGraphV1

param environmentName string
param location string
param tags object

@description('The presenter\'s Entra object ID, empty for none. The presenter plays the Integration operator in demos.')
param presenterPrincipalId string

@description('Easy Auth\'s sign-in callback on the Estate health check, empty before the app is first deployed.')
param healthCheckRedirectUris string = ''

var runRole = 'HealthCheck.Run'
var runRoleId = guid('norvia-health-check', environmentName, runRole)
var readRole = 'HealthCheck.Read'
var readRoleId = guid('norvia-health-check', environmentName, readRole)
var costRole = 'HealthCheck.Cost'
var costRoleId = guid('norvia-health-check', environmentName, costRole)

var reader = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7')
var costManagementReader = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '72fafb9e-0641-4937-9268-a91bfd8191a3')

// The app's own identity, which it reads the estate as, never with the operator's token (ADR 0017) and never the Agent's.
resource healthCheckIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  // infra/provision-estate.ps1 finds it by this prefix.
  name: 'id-health-check-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

// Reader on the estate's resource group: Azure Resource Graph answers for the resources it can read, and Service Bus's management plane
// lists the namespace's queues and subscriptions. Reader reads configuration and nothing else; it holds no data role and writes nothing.
resource healthCheckReadsTheEstate 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, healthCheckIdentity.id, reader)
  properties: {
    principalId: healthCheckIdentity.properties.principalId
    roleDefinitionId: reader
    principalType: 'ServicePrincipal'
  }
}

// Cost Management Reader on the estate's resource group (#504): the cost view reads the group's daily cost per resource. It reads cost and
// budgets and nothing else, and it is the app's, never the Agent's.
resource healthCheckReadsTheEstatesCost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, healthCheckIdentity.id, costManagementReader)
  properties: {
    principalId: healthCheckIdentity.properties.principalId
    roleDefinitionId: costManagementReader
    principalType: 'ServicePrincipal'
  }
}

// What Easy Auth signs Integration operators in to in front of the Estate health check. The app reads the operator's app roles and
// decides for itself what they may do.
resource healthCheck 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-health-check-${environmentName}'
  // infra/provision-estate.ps1 finds it by this name.
  displayName: 'Norvia Estate health check (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  web: {
    // The postdeploy hook adds it once Aspire has deployed the app.
    redirectUris: filter(split(healthCheckRedirectUris, ','), uri => !empty(uri))
    implicitGrantSettings: {
      // Without a client secret Easy Auth asks for an ID token only, which carries the operator's app roles.
      enableIdTokenIssuance: true
    }
  }
  appRoles: [
    {
      id: runRoleId
      value: runRole
      displayName: 'Run an Estate health check'
      description: 'Run a health check of the estate and see what it found. The Integration operator holds it.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
    {
      id: readRoleId
      value: readRole
      displayName: 'Read an Estate health check'
      description: 'Reserved for the report of a health check, for someone who may not run one.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
    {
      id: costRoleId
      value: costRole
      displayName: 'See the estate\'s spend'
      description: 'See the spend page: spend per resource next to Transport Order volume. The Integration operator holds it.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
  ]
}

resource healthCheckPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: healthCheck.appId
  // Entra signs in only users holding one of the app's roles; the app itself refuses those without Run.
  appRoleAssignmentRequired: true
}

// The presenter plays the Integration operator in demos, so holds all three roles.
resource runRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(presenterPrincipalId)) {
  resourceId: healthCheckPrincipal.id
  appRoleId: runRoleId
  principalId: presenterPrincipalId
}

resource readRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(presenterPrincipalId)) {
  resourceId: healthCheckPrincipal.id
  appRoleId: readRoleId
  principalId: presenterPrincipalId
}

resource costRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(presenterPrincipalId)) {
  resourceId: healthCheckPrincipal.id
  appRoleId: costRoleId
  principalId: presenterPrincipalId
}

@description('The app\'s identity, which asks its Agent (Foundry User on the project) and reads the estate.')
output principalId string = healthCheckIdentity.properties.principalId
