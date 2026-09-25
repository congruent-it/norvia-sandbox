// Entra app registrations and the identities that hold their roles (ADR 0005: identity comes from the token).
extension microsoftGraphV1

param environmentName string
param location string
param tags object

@description('The presenter\'s Entra object ID, empty for none. The presenter is the one user who may open the demo UIs.')
param presenterPrincipalId string

@description('Easy Auth\'s sign-in callbacks on the demo UIs, comma separated, empty before the apps are first deployed.')
param demoUiRedirectUris string

var intakeRole = 'TransportOrders.Intake'
var intakeRoleId = guid('norvia-tms-api', environmentName, intakeRole)
var submitRole = 'TransportOrders.Submit'
var submitRoleId = guid('norvia-partner-api', environmentName, submitRole)
var triggerRole = 'TransportOrderIntake.Trigger'
var triggerRoleId = guid('norvia-intake-trigger', environmentName, triggerRole)
var demoUisRole = 'DemoUis.Use'
var demoUisRoleId = guid('norvia-demo-uis', environmentName, demoUisRole)

// Every Persona the Partner portal plays is its own client app registration (ADR 0005). The seed file names them.
var personas = loadJsonContent('../../seed/norvia.seed.json').personas

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

// The Partner portal's identity. The Persona registrations trust it, so the portal gets each Persona's token without a
// client secret. Aspire deploys the portal; the estate owns the identity because the registrations need it.
resource partnerPortalIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-partner-portal-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

// API Management's identity, which it calls the intake Logic App as.
resource apiManagementIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-apim-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource partnerApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-partner-api-${environmentName}'
  displayName: 'Norvia Partner API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-partner-api-${environmentName}'
  ]
  api: {
    // v2.0 tokens carry the Persona's client ID in azp, which API Management maps to a Shipper.
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: submitRoleId
      value: submitRole
      displayName: 'Submit Transport Orders'
      description: 'Book a Transport Order as a Shipper. Only Shipper Personas hold it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource partnerApiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: partnerApi.appId
  // Unlike the TMS, Entra issues a token to any Persona, holding a role or not: a Carrier Persona without the submit role
  // then gets 403 from API Management, which is what the demo shows (P1).
  appRoleAssignmentRequired: false
}

resource personaApps 'Microsoft.Graph/applications@v1.0' = [
  for persona in personas: {
    uniqueName: '${persona.clientAppRegistration}-${environmentName}'
    displayName: 'Norvia Persona ${persona.displayName} (${environmentName})'
    signInAudience: 'AzureADMyOrg'
  }
]

resource personaPrincipals 'Microsoft.Graph/servicePrincipals@v1.0' = [
  for (persona, i) in personas: {
    appId: personaApps[i].appId
  }
]

// No client secrets: a Persona's token is had only by presenting a token of the Partner portal's identity.
resource personaTrustsThePartnerPortal 'Microsoft.Graph/applications/federatedIdentityCredentials@v1.0' = [
  for (persona, i) in personas: {
    name: '${personaApps[i].uniqueName}/partner-portal'
    issuer: '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
    subject: partnerPortalIdentity.properties.principalId
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
]

// Shipper Personas may submit Transport Orders; the others may not.
resource submitRoleGrants 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [
  for (persona, i) in personas: if (persona.party.type == 'Shipper') {
    resourceId: partnerApiPrincipal.id
    appRoleId: submitRoleId
    principalId: personaPrincipals[i].id
  }
]

// What Easy Auth on the intake Logic App validates API Management's tokens for.
resource intakeTrigger 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-intake-trigger-${environmentName}'
  displayName: 'Norvia intake Logic App trigger (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-intake-trigger-${environmentName}'
  ]
  appRoles: [
    {
      id: triggerRoleId
      value: triggerRole
      displayName: 'Trigger Transport Order intake'
      description: 'Call the intake Logic App\'s trigger. Only API Management holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource intakeTriggerPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: intakeTrigger.appId
  // Entra issues tokens for the trigger only to API Management.
  appRoleAssignmentRequired: true
}

resource triggerRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: intakeTriggerPrincipal.id
  appRoleId: triggerRoleId
  principalId: apiManagementIdentity.properties.principalId
}

// What Easy Auth signs the presenter in to in front of the TMS UI and the Partner portal (ADR 0024). The apps have no
// login of their own; this is a gate for the demo environment.
resource demoUis 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-demo-uis-${environmentName}'
  displayName: 'Norvia demo UIs (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  web: {
    // The postdeploy hook adds them once Aspire has deployed the apps.
    redirectUris: filter(split(demoUiRedirectUris, ','), uri => !empty(uri))
    implicitGrantSettings: {
      // Without a client secret Easy Auth asks for an ID token only, which a gate needs and nothing more.
      enableIdTokenIssuance: true
    }
  }
  appRoles: [
    {
      id: demoUisRoleId
      value: demoUisRole
      displayName: 'Use the demo UIs'
      description: 'Open the TMS UI and the Partner portal in Azure. Only the presenter holds it.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
  ]
}

resource demoUisPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: demoUis.appId
  // Entra signs in only the users assigned to it, so anyone else is refused before either app runs.
  appRoleAssignmentRequired: true
}

resource demoUisRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(presenterPrincipalId)) {
  resourceId: demoUisPrincipal.id
  appRoleId: demoUisRoleId
  principalId: presenterPrincipalId
}

output entraIssuer string = '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
output entraTenantId string = tenant().tenantId
output tmsApiClientId string = tmsApi.appId
output intakeClientId string = intakeIdentity.properties.clientId
output intakeIdentityId string = intakeIdentity.id
output intakeIdentityName string = intakeIdentity.name
output partnerApiClientId string = partnerApi.appId
@description('API Management\'s map from a Persona\'s client ID (the token\'s azp) to its Shipper ID: clientId=shipperId, empty for a Persona that is no Shipper.')
output shipperByClientId array = [
  for (persona, i) in personas: persona.party.type == 'Shipper' ? '${personaApps[i].appId}=${persona.party.id}' : ''
]
@description('Each Persona\'s client ID: personaId=clientId.')
output personaClientIds array = [for (persona, i) in personas: '${persona.id}=${personaApps[i].appId}']
output partnerPortalIdentityId string = partnerPortalIdentity.id
output partnerPortalIdentityName string = partnerPortalIdentity.name
output partnerPortalPrincipalId string = partnerPortalIdentity.properties.principalId
output partnerPortalClientId string = partnerPortalIdentity.properties.clientId
output apiManagementIdentityName string = apiManagementIdentity.name
output apiManagementPrincipalId string = apiManagementIdentity.properties.principalId
output apiManagementClientId string = apiManagementIdentity.properties.clientId
output intakeTriggerClientId string = intakeTrigger.appId
output intakeTriggerAudience string = intakeTrigger.identifierUris[0]
output demoUisClientId string = demoUis.appId
