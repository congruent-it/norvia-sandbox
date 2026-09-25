// Entra app registrations and the identities that hold their roles (ADR 0005: identity comes from the token).
extension microsoftGraphV1

param environmentName string
param location string
param tags object

var intakeRole = 'TransportOrders.Intake'
var intakeRoleId = guid('norvia-tms-api', environmentName, intakeRole)

// The intake Logic App's identity, which it runs as. It is the one principal holding the TMS intake role, so nothing
// reaches the TMS without passing the integration layer.
resource intakeIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-intake-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource tmsApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-tms-api-${environmentName}'
  displayName: 'Norvia TMS API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  // The tenant's default policy only accepts an api:// URI with the tenant or app ID in it.
  identifierUris: [
    'api://${tenant().tenantId}/norvia-tms-${environmentName}'
  ]
  api: {
    // v2.0 tokens carry the caller's client ID in azp, which the TMS maps to an intake channel.
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: intakeRoleId
      value: intakeRole
      displayName: 'Transport Order intake'
      description: 'Submit Transport Orders to the TMS. Only the intake Logic App holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource tmsApiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: tmsApi.appId
  // Entra issues tokens for the TMS only to principals granted one of its roles.
  appRoleAssignmentRequired: true
}

resource intakeRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: intakeRoleId
  principalId: intakeIdentity.properties.principalId
}

output entraIssuer string = '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
output tmsApiClientId string = tmsApi.appId
output intakeClientId string = intakeIdentity.properties.clientId
output intakeIdentityId string = intakeIdentity.id
output intakeIdentityName string = intakeIdentity.name
