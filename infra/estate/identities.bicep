// Entra app registrations and the identities that hold their roles (ADR 0005: identity comes from the token).
extension microsoftGraphV1

param environmentName string
param location string
param tags object

@description('The presenter\'s Entra object ID, empty for none. The presenter is the one user who may open the demo UIs.')
param presenterPrincipalId string

@description('Easy Auth\'s sign-in callbacks on the demo UIs, comma separated, empty before the apps are first deployed.')
param demoUiRedirectUris string

@description('Easy Auth\'s sign-in callback on the Integration console, empty before the console is first deployed.')
param integrationConsoleRedirectUris string

@description('Easy Auth\'s sign-in callback on the Driver app, empty before the Driver app is first deployed.')
param driverAppRedirectUris string

var intakeRole = 'TransportOrders.Intake'
var intakeRoleId = guid('norvia-tms-api', environmentName, intakeRole)
var manageRole = 'TransportOrders.Manage'
var manageRoleId = guid('norvia-tms-api', environmentName, manageRole)
var answerRole = 'CarrierBookings.Answer'
var answerRoleId = guid('norvia-tms-api', environmentName, answerRole)
var reportRole = 'CarrierBookings.Report'
var reportRoleId = guid('norvia-tms-api', environmentName, reportRole)
var shipperNotificationReportRole = 'ShipperNotifications.Report'
var shipperNotificationReportRoleId = guid('norvia-tms-api', environmentName, shipperNotificationReportRole)
var tripReportDeliveryRole = 'Trips.ReportDelivery'
var tripReportDeliveryRoleId = guid('norvia-tms-api', environmentName, tripReportDeliveryRole)
var tripStartRole = 'Trips.Start'
var tripStartRoleId = guid('norvia-tms-api', environmentName, tripStartRole)
var tripEndRole = 'Trips.End'
var tripEndRoleId = guid('norvia-tms-api', environmentName, tripEndRole)
var driverReportOutcomeRole = 'DriverReports.ReportOutcome'
var driverReportOutcomeRoleId = guid('norvia-driver-app-api', environmentName, driverReportOutcomeRole)
var recordRole = 'TrackingEvents.Record'
var recordRoleId = guid('norvia-tms-api', environmentName, recordRole)
var recordProofRole = 'ProofsOfDelivery.Record'
var recordProofRoleId = guid('norvia-tms-api', environmentName, recordProofRole)
var shipperTrackingReadRole = 'ShipperTracking.Read'
var shipperTrackingReadRoleId = guid('norvia-tms-api', environmentName, shipperTrackingReadRole)
var trackingSubmitRole = 'Tracking.Submit'
var trackingSubmitRoleId = guid('norvia-partner-api', environmentName, trackingSubmitRole)
var proofSubmitRole = 'ProofsOfDelivery.Submit'
var proofSubmitRoleId = guid('norvia-partner-api', environmentName, proofSubmitRole)
var respondRole = 'CarrierBookings.Respond'
var respondRoleId = guid('norvia-partner-api', environmentName, respondRole)
var trackingReadRole = 'Tracking.Read'
var trackingReadRoleId = guid('norvia-partner-api', environmentName, trackingReadRole)
var submitRole = 'TransportOrders.Submit'
var submitRoleId = guid('norvia-partner-api', environmentName, submitRole)
var triggerRole = 'TransportOrderIntake.Trigger'
var triggerRoleId = guid('norvia-intake-trigger', environmentName, triggerRole)
var answerTriggerRole = 'CarrierAnswer.Trigger'
var answerTriggerRoleId = guid('norvia-answer-trigger', environmentName, answerTriggerRole)
var demoUisRole = 'DemoUis.Use'
var demoUisRoleId = guid('norvia-demo-uis', environmentName, demoUisRole)
var consoleReadRole = 'IntegrationConsole.Read'
var consoleReadRoleId = guid('norvia-integration-console', environmentName, consoleReadRole)
var consoleResubmitRole = 'IntegrationConsole.Resubmit'
var consoleResubmitRoleId = guid('norvia-integration-console', environmentName, consoleResubmitRole)

// Every active Driver the seed file names is one app role of the Driver app's registration, with the Driver ID as its value.
var drivers = filter(loadJsonContent('../../seed/norvia.seed.json').drivers, driver => driver.active)

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
    {
      id: manageRoleId
      value: manageRole
      displayName: 'Manage Transport Orders'
      description: 'Change Transport Orders as Norvia\'s staff do in the TMS UI, such as re-rating one. Only the TMS UI holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: answerRoleId
      value: answerRole
      displayName: 'Answer Carrier bookings'
      description: 'Record a Carrier\'s confirmation or rejection of a Carrier booking (P4). Only the Carrier-answer Logic App holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: reportRoleId
      value: reportRole
      displayName: 'Report Carrier bookings'
      description: 'Report that a Carrier booking\'s request, or its cancellation or withdrawal, was sent to the Carrier or never reached it (P4, P7). Only the P4 booking Logic App and the P7 Carrier cancellation Logic App hold it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: shipperNotificationReportRoleId
      value: shipperNotificationReportRole
      displayName: 'Report Shipper notifications'
      description: 'Report that a Shipper notification was acknowledged by the Shipper\'s endpoint, or never reached it (P7). Only the P7 Shipper notification Logic App holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: tripStartRoleId
      value: tripStartRole
      displayName: 'Start a Trip'
      description: 'Start a Dispatched Trip for its Driver on the Trip\'s day (P12). Only the P12 Driver reports Logic App holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: tripEndRoleId
      value: tripEndRole
      displayName: 'End a Trip'
      description: 'End an In progress Trip for its Driver once every Stop has an outcome (P12). Only the P12 Driver reports Logic App will hold it, once it is built.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: tripReportDeliveryRoleId
      value: tripReportDeliveryRole
      displayName: 'Report Trip delivery'
      description: 'Report that a version of a Trip was accepted by the Driver app, or never reached it (P11). Only the P11 Trip dispatch Logic App holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: recordRoleId
      value: recordRole
      displayName: 'Record Tracking events'
      description: 'Record a Tracking event reported for a Shipment (P5). Only the Carrier-tracking Logic App holds it, and later the Driver reports Logic App (P12).'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: shipperTrackingReadRoleId
      value: shipperTrackingReadRole
      displayName: 'Read Shipper tracking'
      description: 'Read a Shipper\'s Transport Orders with their Tracking status (P8). Only API Management holds it, and the TMS trusts the Shipper ID header only from it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: recordProofRoleId
      value: recordProofRole
      displayName: 'Record Proofs of delivery'
      description: 'Record a Proof of delivery with its files for a Shipment (P6). Only the Carrier Proof of delivery Logic App will hold it, and later the Driver reports Logic App (P12).'
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

// The P4 booking Logic App's identity, which it runs as. It is the one principal holding the report role, so nothing
// reports a Carrier booking's delivery to the TMS without passing the integration layer.
resource bookingIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-booking-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource reportRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: reportRoleId
  principalId: bookingIdentity.properties.principalId
}

// The P7 Shipper notification Logic App's identity, which it runs as. It is the one principal holding the Shipper notification
// report role, so nothing reports a Shipper notification's delivery to the TMS without passing the integration layer.
resource shipperNotificationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-shipper-notification-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

// The P7 Carrier cancellation Logic App's identity, which it runs as. It holds the Carrier booking report role, like the booking
// identity, because it reports a cancellation told or not delivered on the same Carrier booking operations. It holds no other role.
resource carrierCancellationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-carrier-cancellation-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource carrierCancellationReportRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: reportRoleId
  principalId: carrierCancellationIdentity.properties.principalId
}

resource shipperNotificationReportRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: shipperNotificationReportRoleId
  principalId: shipperNotificationIdentity.properties.principalId
}

// The P11 Trip dispatch Logic App's identity, which it runs as. It is the one principal holding the Trip delivery report role, so
// nothing reports a Trip version's delivery to the TMS without passing the integration layer. It holds no other role.
resource tripDispatchIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-trip-dispatch-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource tripDispatchReportRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: tripReportDeliveryRoleId
  principalId: tripDispatchIdentity.properties.principalId
}

// The Driver app's identity, which its API runs as in Azure. It reads the credential its Trip endpoint asks of its caller from
// Key Vault, and holds no role on the TMS: the Driver app reaches the TMS only through the integration layer (ADR 0001).
resource driverAppIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-driver-app-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

// The P12 Driver reports Logic App's identity, which it runs as. It is the one principal holding the Trip start role and, with the Carrier-tracking
// Logic App, the Tracking event record role (and, as they are built, the Trip end and Proof of delivery record roles), so nothing a Driver reports
// reaches the TMS without passing the integration layer, and the one principal holding the Driver app's outcome role.
resource driverReportsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-driver-reports-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource driverReportsStartRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: tripStartRoleId
  principalId: driverReportsIdentity.properties.principalId
}

// The Tracking event record role, which the Carrier-tracking Logic App holds too: the Driver reports Logic App records a Driver's Stop report as a
// Tracking event of Own fleet with it.
resource driverReportsRecordRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: recordRoleId
  principalId: driverReportsIdentity.properties.principalId
}

// The Proof of delivery record role, which the Carrier Proof of delivery Logic App holds too: the Driver reports Logic App records a Driver's Proof of delivery
// as one of Own fleet with it, in the multipart call that P6 defines.
resource driverReportsRecordProofRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: recordProofRoleId
  principalId: driverReportsIdentity.properties.principalId
}

// The Driver app API's registration, which is the audience of the Driver reports Logic App's token for the Driver app's outcome endpoint
// (ADR 0005). Entra issues one only to a principal granted the role.
resource driverAppApi 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-driver-app-api-${environmentName}'
  displayName: 'Norvia Driver app API (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-driver-app-api-${environmentName}'
  ]
  api: {
    requestedAccessTokenVersion: 2
  }
  appRoles: [
    {
      id: driverReportOutcomeRoleId
      value: driverReportOutcomeRole
      displayName: 'Report the outcome of a Driver report'
      description: 'Tell the Driver app whether the TMS recorded a report of a Driver, refused it, or was never reached (P12). Only the P12 Driver reports Logic App holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource driverAppApiPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: driverAppApi.appId
  appRoleAssignmentRequired: true
}

resource driverReportsOutcomeRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: driverAppApiPrincipal.id
  appRoleId: driverReportOutcomeRoleId
  principalId: driverReportsIdentity.properties.principalId
}

// The Carrier-answer Logic App's identity, which it runs as. It is the one principal holding the answer role, so nothing
// records a Carrier's answer in the TMS without passing the integration layer.
resource answerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-answer-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource answerRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: answerRoleId
  principalId: answerIdentity.properties.principalId
}

// The Carrier-tracking Logic App's identity, which it runs as. It is the one principal holding the record role, so nothing
// records a Carrier's Tracking event in the TMS without passing the integration layer.
resource trackingIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-tracking-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource recordRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: recordRoleId
  principalId: trackingIdentity.properties.principalId
}

// The Carrier Proof of delivery Logic App's identity, which it runs as. It is the one principal holding the Proof of delivery
// record role (P12's Driver reports Logic App will hold it too), so nothing records a Carrier's Proof of delivery in the TMS
// without passing the integration layer.
resource proofOfDeliveryIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-proof-of-delivery-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource recordProofRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: recordProofRoleId
  principalId: proofOfDeliveryIdentity.properties.principalId
}

// The TMS UI's identity, which it calls the TMS API as. It is the one principal holding the manage role, so only the
// TMS UI re-rates. Aspire deploys the TMS UI; the estate owns the identity because it grants it the role.
resource tmsUiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-tms-ui-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

resource manageRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: manageRoleId
  principalId: tmsUiIdentity.properties.principalId
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

// API Management is the one principal holding the Shipper tracking role, so a Shipper-scoped read reaches the TMS only
// through the Shipper's token check at the edge.
resource shipperTrackingReadRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: tmsApiPrincipal.id
  appRoleId: shipperTrackingReadRoleId
  principalId: apiManagementIdentity.properties.principalId
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
    {
      id: trackingReadRoleId
      value: trackingReadRole
      displayName: 'Read Shipper tracking'
      description: 'Read a Shipper\'s Transport Orders with their Tracking status and progress (P8). Only Shipper Personas hold it, so a Carrier Persona gets 403.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: respondRoleId
      value: respondRole
      displayName: 'Answer Carrier bookings'
      description: 'Confirm or reject a Carrier booking request as a Carrier. Only Carrier Personas hold it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: trackingSubmitRoleId
      value: trackingSubmitRole
      displayName: 'Submit Carrier tracking'
      description: 'Report what happened to a Shipment as a Carrier, with a tracking message. Only Carrier Personas hold it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
    {
      id: proofSubmitRoleId
      value: proofSubmitRole
      displayName: 'Submit Carrier Proofs of delivery'
      description: 'Prove a delivery as a Carrier, with a Proof of delivery and its files. Only Carrier Personas hold it.'
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

// Shipper Personas may read their tracking too; the Carrier Personas may not, and API Management refuses them with 403 (P8).
resource trackingReadRoleGrants 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [
  for (persona, i) in personas: if (persona.party.type == 'Shipper') {
    resourceId: partnerApiPrincipal.id
    appRoleId: trackingReadRoleId
    principalId: personaPrincipals[i].id
  }
]

// Carrier Personas may answer Carrier bookings; the others may not.
resource respondRoleGrants 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [
  for (persona, i) in personas: if (persona.party.type == 'Carrier') {
    resourceId: partnerApiPrincipal.id
    appRoleId: respondRoleId
    principalId: personaPrincipals[i].id
  }
]

// Carrier Personas may report tracking too; the others may not.
resource trackingSubmitRoleGrants 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [
  for (persona, i) in personas: if (persona.party.type == 'Carrier') {
    resourceId: partnerApiPrincipal.id
    appRoleId: trackingSubmitRoleId
    principalId: personaPrincipals[i].id
  }
]

// Carrier Personas may submit Proofs of delivery too; the others may not.
resource proofSubmitRoleGrants 'Microsoft.Graph/appRoleAssignedTo@v1.0' = [
  for (persona, i) in personas: if (persona.party.type == 'Carrier') {
    resourceId: partnerApiPrincipal.id
    appRoleId: proofSubmitRoleId
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

// What Easy Auth on the Carrier-answer Logic App validates API Management's tokens for.
resource answerTrigger 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-answer-trigger-${environmentName}'
  displayName: 'Norvia Carrier-answer Logic App trigger (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  identifierUris: [
    'api://${tenant().tenantId}/norvia-answer-trigger-${environmentName}'
  ]
  appRoles: [
    {
      id: answerTriggerRoleId
      value: answerTriggerRole
      displayName: 'Trigger Carrier answers'
      description: 'Call the Carrier-answer Logic App\'s trigger. Only API Management holds it.'
      allowedMemberTypes: [
        'Application'
      ]
      isEnabled: true
    }
  ]
}

resource answerTriggerPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: answerTrigger.appId
  // Entra issues tokens for the trigger only to API Management.
  appRoleAssignmentRequired: true
}

resource answerTriggerRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = {
  resourceId: answerTriggerPrincipal.id
  appRoleId: answerTriggerRoleId
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

// What Easy Auth signs Integration operators in to in front of the Integration console (ADR 0026). Unlike the demo UIs'
// gate, the console reads the operator's app roles and decides for itself what they may see.
resource integrationConsole 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-integration-console-${environmentName}'
  displayName: 'Norvia Integration console (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  web: {
    // The postdeploy hook adds it once Aspire has deployed the console.
    redirectUris: filter(split(integrationConsoleRedirectUris, ','), uri => !empty(uri))
    implicitGrantSettings: {
      // Without a client secret Easy Auth asks for an ID token only, which carries the operator's app roles.
      enableIdTokenIssuance: true
    }
  }
  appRoles: [
    {
      id: consoleReadRoleId
      value: consoleReadRole
      displayName: 'Read in the Integration console'
      description: 'Search Message trails and open run details and message bodies in the Integration console.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
    {
      id: consoleResubmitRoleId
      value: consoleResubmitRole
      displayName: 'Resubmit in the Integration console'
      description: 'Also resubmit and discard dead-lettered messages in the Integration console.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
  ]
}

resource integrationConsolePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: integrationConsole.appId
  // Entra signs in only users holding one of the console's roles; the console itself refuses those without Read.
  appRoleAssignmentRequired: true
}

// The presenter plays the Integration operator in demos, so holds both roles.
resource consoleReadRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(presenterPrincipalId)) {
  resourceId: integrationConsolePrincipal.id
  appRoleId: consoleReadRoleId
  principalId: presenterPrincipalId
}

resource consoleResubmitRoleGrant 'Microsoft.Graph/appRoleAssignedTo@v1.0' = if (!empty(presenterPrincipalId)) {
  resourceId: integrationConsolePrincipal.id
  appRoleId: consoleResubmitRoleId
  principalId: presenterPrincipalId
}

// What Easy Auth signs Drivers in to in front of the Driver app (ADR 0037). The Driver ID is the user's app role: each
// seeded Driver is one role, with their Driver ID as its value, and infra/create-driver-test-users.sh makes a test user
// for each and assigns it. The Driver app takes the Driver ID from that role in the token, never from the request.
resource driverApp 'Microsoft.Graph/applications@v1.0' = {
  uniqueName: 'norvia-driver-app-${environmentName}'
  displayName: 'Norvia Driver app (${environmentName})'
  signInAudience: 'AzureADMyOrg'
  web: {
    // The postdeploy hook adds it once Aspire has deployed the Driver app.
    redirectUris: filter(split(driverAppRedirectUris, ','), uri => !empty(uri))
    implicitGrantSettings: {
      // Without a client secret Easy Auth asks for an ID token only, which carries the Driver's app role.
      enableIdTokenIssuance: true
    }
  }
  appRoles: [
    for driver in drivers: {
      id: guid('norvia-driver-app', environmentName, driver.id)
      value: driver.id
      displayName: 'Drive as ${driver.name}'
      description: 'Sign in to the Driver app as Driver ${driver.id}, ${driver.name}, and see their Trips.'
      allowedMemberTypes: [
        'User'
      ]
      isEnabled: true
    }
  ]
}

resource driverAppPrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = {
  appId: driverApp.appId
  // Entra signs in only the users assigned one of the Driver roles, so a stranger never reaches the Driver app.
  appRoleAssignmentRequired: true
}

// The console's own identity, which it reads the estate as, never with the operator's token (ADR 0017). Aspire deploys
// the console; the estate owns the identity because the estate grants it what it may read.
resource integrationConsoleIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-integration-console-${uniqueString(resourceGroup().id)}'
  location: location
  tags: tags
}

output entraIssuer string = '${environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
output entraTenantId string = tenant().tenantId
output tmsApiClientId string = tmsApi.appId
output intakeClientId string = intakeIdentity.properties.clientId
output intakeIdentityId string = intakeIdentity.id
output intakeIdentityName string = intakeIdentity.name
output answerIdentityId string = answerIdentity.id
output answerIdentityName string = answerIdentity.name
output answerClientId string = answerIdentity.properties.clientId
output trackingIdentityId string = trackingIdentity.id
output trackingIdentityName string = trackingIdentity.name
output trackingPrincipalId string = trackingIdentity.properties.principalId
output trackingClientId string = trackingIdentity.properties.clientId
output proofOfDeliveryIdentityId string = proofOfDeliveryIdentity.id
output proofOfDeliveryIdentityName string = proofOfDeliveryIdentity.name
output proofOfDeliveryPrincipalId string = proofOfDeliveryIdentity.properties.principalId
output proofOfDeliveryClientId string = proofOfDeliveryIdentity.properties.clientId
output bookingIdentityId string = bookingIdentity.id
output bookingIdentityName string = bookingIdentity.name
output bookingPrincipalId string = bookingIdentity.properties.principalId
output bookingClientId string = bookingIdentity.properties.clientId
output shipperNotificationIdentityId string = shipperNotificationIdentity.id
output shipperNotificationIdentityName string = shipperNotificationIdentity.name
output shipperNotificationPrincipalId string = shipperNotificationIdentity.properties.principalId
output shipperNotificationClientId string = shipperNotificationIdentity.properties.clientId
output carrierCancellationIdentityId string = carrierCancellationIdentity.id
output carrierCancellationIdentityName string = carrierCancellationIdentity.name
output carrierCancellationPrincipalId string = carrierCancellationIdentity.properties.principalId
output carrierCancellationClientId string = carrierCancellationIdentity.properties.clientId
output tmsUiIdentityName string = tmsUiIdentity.name
output tmsUiClientId string = tmsUiIdentity.properties.clientId
output partnerApiClientId string = partnerApi.appId
@description('API Management\'s map from a Persona\'s client ID (the token\'s azp) to its Shipper ID: clientId=shipperId, empty for a Persona that is no Shipper.')
output shipperByClientId array = [
  for (persona, i) in personas: persona.party.type == 'Shipper' ? '${personaApps[i].appId}=${persona.party.id}' : ''
]
@description('API Management\'s map from a Persona\'s client ID (the token\'s azp) to its Carrier ID: clientId=carrierId, empty for a Persona that is no Carrier.')
output carrierByClientId array = [
  for (persona, i) in personas: persona.party.type == 'Carrier' ? '${personaApps[i].appId}=${persona.party.id}' : ''
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
output answerTriggerClientId string = answerTrigger.appId
output answerTriggerAudience string = answerTrigger.identifierUris[0]
output demoUisClientId string = demoUis.appId
output integrationConsoleClientId string = integrationConsole.appId
output driverAppRegistrationClientId string = driverApp.appId
output integrationConsoleIdentityName string = integrationConsoleIdentity.name
output integrationConsolePrincipalId string = integrationConsoleIdentity.properties.principalId
output tripDispatchIdentityId string = tripDispatchIdentity.id
output tripDispatchIdentityName string = tripDispatchIdentity.name
output tripDispatchPrincipalId string = tripDispatchIdentity.properties.principalId
output tripDispatchClientId string = tripDispatchIdentity.properties.clientId
output driverAppIdentityName string = driverAppIdentity.name
output driverAppPrincipalId string = driverAppIdentity.properties.principalId
output driverAppClientId string = driverAppIdentity.properties.clientId
output driverReportsIdentityId string = driverReportsIdentity.id
output driverReportsIdentityName string = driverReportsIdentity.name
output driverReportsPrincipalId string = driverReportsIdentity.properties.principalId
output driverReportsClientId string = driverReportsIdentity.properties.clientId
output driverAppApiClientId string = driverAppApi.appId
