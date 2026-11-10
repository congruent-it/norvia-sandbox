// The Intake agent's Booking draft in Azure (ADR 0040, #459): the API Management operation `booking-drafts` of the Norvia Partner API, the
// host it routes to, and what keeps that host narrow.
//
//   Shipper Persona --(subscription key, Entra token with TransportOrders.Draft)--> API Management (policy: checks and routes)
//     --(API Management's token for the host's API, role BookingDrafts.Create)--> the host (Container App, Norvia.Integration.BookingDraft.Api)
//     --(the host's own managed identity, Foundry User on norvia-intake)--> the Intake agent published in Foundry
//
// Only API Management's identity holds the host's one role, so nobody else can call the host (as ADR 0020 does for the intake Logic App). The
// host's identity holds Foundry User on the Intake project and nothing else (infra/estate/foundry.bicep); the Intake agent's own identity holds no
// role at all, as it has no tool. The operation logs no body: the service's diagnostics (partner-api.bicep) have no body setting, and the policy
// reads the body only for its size.
//
// Like the other agent modules it has no outputs of the template bar the identity's principal for the Foundry User role (the estate is at the
// 64 a template may have): infra/provision-estate.ps1 finds the app registration and the identity by their names.
extension microsoftGraphV1

param environmentName string
param location string
param tags object

param apiManagementName string

@description('API Management\'s identity, which calls the host (it holds the one role on the host\'s API).')
param apiManagementPrincipalId string

@description('''The host\'s address, empty before the apps are first deployed. infra/deploy-agents.ps1 stores it (ESTATE_BOOKING_DRAFT_BASE_URL) and points the
backend at it; the placeholder fails every call with a gateway error, which the policy turns into "the assistant is unavailable".''')
param bookingDraftBaseUrl string = ''

var role = 'BookingDrafts.Create'
var roleId = guid('norvia-booking-draft-api', environmentName, role)

// The host's own identity, which it asks Foundry as. The estate owns it because the estate grants it what it may call; Aspire deploys the app.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  // infra/provision-estate.ps1 finds it by this prefix.
  name: 'id-booking-draft-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

// The host's API: the audience of the token API Management sends it. The host takes this token and nothing else, and the role is the only one on it.
resource api 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-booking-draft-api-${environmentName}'
  // infra/provision-estate.ps1 finds it by this name.
  displayName: 'Norvia booking draft API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-booking-draft-api-${environmentName}'
  ]
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: roleId
      value: role
      displayName: 'Ask the Intake agent for a Booking draft'
      description: 'Call the Booking draft host. API Management\'s identity holds it, on behalf of the Shipper; no Persona, operator or Agent does.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource apiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: api.appId
  appRoleAssignmentRequired: true
}

resource apiManagementCallsTheHost 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: apiPrincipal.id
  appRoleId: roleId
  principalId: apiManagementPrincipalId
}

resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' existing = {
  name: apiManagementName
}

resource audience 'Microsoft.ApiManagement/service/namedValues@2024-05-01' = {
  parent: apiManagement
  name: 'booking-draft-api-client-id'
  properties: {
    displayName: 'booking-draft-api-client-id'
    value: api.appId
  }
}

// The host as a backend. The address comes after the host is deployed; the Bicep keeps what the deploy script stored.
resource host 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'booking-draft-host'
  properties: {
    description: 'The Booking draft host (Norvia.Integration.BookingDraft.Api). The policy names the route.'
    url: empty(bookingDraftBaseUrl) ? 'https://booking-draft-host.invalid' : bookingDraftBaseUrl
    protocol: 'http'
  }
}

resource partnerApi 'Microsoft.ApiManagement/service/apis@2024-05-01' existing = {
  parent: apiManagement
  name: 'norvia-partner-api'
}

resource bookingDrafts 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  parent: partnerApi
  name: 'booking-drafts'
  properties: {
    displayName: 'Create Booking draft'
    description: 'Fill in a booking from the text of an email as a Shipper, with the Intake agent (contracts/partner-api). Nothing is booked or stored.'
    method: 'POST'
    urlTemplate: '/booking-drafts'
  }
}

resource bookingDraftsPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = {
  parent: bookingDrafts
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('partner-api/create-booking-draft.policy.xml')
  }
  dependsOn: [
    audience
    host
    apiManagementCallsTheHost
  ]
}

output principalId string = identity.properties.principalId
