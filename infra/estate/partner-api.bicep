// API Management and the Norvia Partner API (P1.10): partners book here, with a subscription key and an Entra token
// (ADR 0005). The policy in partner-api/ checks both, takes the Shipper from the token and passes the booking to the
// intake Logic App, as API Management's own identity.

param location string
param tags object

@description('Who API Management names as its publisher. Developer tier requires one; nobody is emailed in the sandbox.')
param publisherEmail string = 'integration@norvia.example'

@description('API Management\'s identity, which the intake Logic App\'s Easy Auth accepts.')
param apiManagementIdentityName string

@description('The Norvia Partner API app registration, the audience of the partners\' tokens.')
param partnerApiClientId string

@description('The Persona client IDs mapped to their Shipper IDs, as clientId=shipperId pairs separated by semicolons.')
param shippersByClientId string

@description('The Personas, whose subscriptions are created here: personaId=clientId pairs.')
param personaClientIds array

@description('The Carrier client IDs mapped to their Carrier IDs, as clientId=carrierId pairs separated by semicolons.')
param carriersByClientId string

@description('The intake Logic App\'s host name, and the audience of the tokens its Easy Auth accepts.')
param intakeLogicAppHostName string
param intakeTriggerAudience string

@description('The Carrier-answer Logic App\'s host name, and the audience of the tokens its Easy Auth accepts.')
param answerLogicAppHostName string
param answerTriggerAudience string

@description('The Service Bus namespace and the tracking queue API Management puts a Carrier\'s tracking message on.')
param serviceBusName string
param trackingQueueName string

@description('The Proof of delivery queue API Management puts a Carrier\'s Proof of delivery reference on, and the inbound container (in the storage account of that name) it stores the body in first (ADR 0009).')
param proofOfDeliveryQueueName string
param inboundStorageAccountName string
param inboundContainerName string

param applicationInsightsName string

@description('The Partner portal\'s identity, the one principal that may read the Personas\' subscription keys.')
param partnerPortalPrincipalId string

@description('The P4 booking Logic App\'s identity, which may read the Carrier endpoints\' credentials and nothing else in the vault.')
param bookingPrincipalId string

@description('The P7 Shipper notification Logic App\'s identity, which may read the Shipper endpoints\' credentials and nothing else in the vault.')
param shipperNotificationPrincipalId string

@secure()
@description('''A random value the Carrier and Shipper endpoints' credentials are derived from, one per Persona. The deployment hook makes it
once and keeps it in the azd environment, so deploying the estate again keeps the credentials the apps already hold.''')
param carrierEndpointKeySeed string

var resourceToken = uniqueString(resourceGroup().id)

resource apiManagementIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  name: apiManagementIdentityName
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

// Developer tier (ADR 0021): no time limit on a request, so the intake Logic App's retries of the TMS still end in its own answer.
// Its first deployment takes about 45 minutes.
resource apiManagement 'Microsoft.ApiManagement/service@2024-05-01' = {
  name: 'apim-norvia-${resourceToken}'
  location: location
  tags: tags
  sku: {
    name: 'Developer'
    capacity: 1
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiManagementIdentity.id}': {}
    }
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: 'Norvia Logistics B.V.'
  }
}

// The values the policy reads. None is a secret.
var namedValues = {
  'entra-tenant-id': tenant().tenantId
  'partner-api-client-id': partnerApiClientId
  'shippers-by-client-id': shippersByClientId
  'carriers-by-client-id': carriersByClientId
  'intake-trigger-audience': intakeTriggerAudience
  'answer-trigger-audience': answerTriggerAudience
  'api-management-identity-client-id': apiManagementIdentity.properties.clientId
  'service-bus-namespace': serviceBusName
  'tracking-queue-name': trackingQueueName
  'proof-of-delivery-queue-name': proofOfDeliveryQueueName
  'inbound-storage-account': inboundStorageAccountName
  'inbound-container-name': inboundContainerName
}

resource policyValues 'Microsoft.ApiManagement/service/namedValues@2024-05-01' = [
  // The loop runs over the names, which are known before the deployment starts; the values aren't.
  for name in ['entra-tenant-id', 'partner-api-client-id', 'shippers-by-client-id', 'carriers-by-client-id', 'intake-trigger-audience', 'answer-trigger-audience', 'api-management-identity-client-id', 'service-bus-namespace', 'tracking-queue-name', 'proof-of-delivery-queue-name', 'inbound-storage-account', 'inbound-container-name']: {
    parent: apiManagement
    name: name
    properties: {
      displayName: name
      value: namedValues[name]
    }
  }
]

// Diagnostics go to the estate's Application Insights (coding guidelines §7), sent as API Management's identity.
var monitoringMetricsPublisher = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '3913510d-42f4-4e42-8a64-420c390055eb')

resource publishesTelemetry 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(applicationInsights.id, apiManagementIdentity.id, monitoringMetricsPublisher)
  scope: applicationInsights
  properties: {
    principalId: apiManagementIdentity.properties.principalId
    roleDefinitionId: monitoringMetricsPublisher
    principalType: 'ServicePrincipal'
  }
}

resource logger 'Microsoft.ApiManagement/service/loggers@2024-05-01' = {
  parent: apiManagement
  name: 'estate-application-insights'
  properties: {
    loggerType: 'applicationInsights'
    resourceId: applicationInsights.id
    credentials: {
      connectionString: applicationInsights.properties.ConnectionString
      identityClientId: apiManagementIdentity.properties.clientId
    }
  }
  dependsOn: [
    publishesTelemetry
  ]
}

// Every call, the policy's traces included (verbosity information), with the correlation ID on the call to the Logic App
// and on the answer. No bodies: a booking holds the Consignee's personal data.
resource diagnostics 'Microsoft.ApiManagement/service/diagnostics@2024-05-01' = {
  parent: apiManagement
  name: 'applicationinsights'
  properties: {
    loggerId: logger.id
    alwaysLog: 'allErrors'
    verbosity: 'information'
    httpCorrelationProtocol: 'W3C'
    logClientIp: false
    sampling: {
      samplingType: 'fixed'
      percentage: 100
    }
    frontend: {
      response: {
        headers: [
          'X-Correlation-ID'
        ]
      }
    }
    backend: {
      request: {
        headers: [
          'X-Correlation-ID'
          'X-Shipper-ID'
          'X-Carrier-ID'
        ]
      }
    }
  }
}

resource intakeLogicApp 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'intake-logic-app'
  properties: {
    description: 'The intake Logic App (P1.9). The policy names the trigger.'
    url: 'https://${intakeLogicAppHostName}'
    protocol: 'http'
  }
}

resource answerLogicApp 'Microsoft.ApiManagement/service/backends@2024-05-01' = {
  parent: apiManagement
  name: 'answer-logic-app'
  properties: {
    description: 'The Carrier-answer Logic App (P4.7). The policy names the trigger.'
    url: 'https://${answerLogicAppHostName}'
    protocol: 'http'
  }
}

resource partnerApi 'Microsoft.ApiManagement/service/apis@2024-05-01' = {
  parent: apiManagement
  name: 'norvia-partner-api'
  properties: {
    displayName: 'Norvia Partner API'
    path: 'partner/v1'
    protocols: [
      'https'
    ]
    subscriptionRequired: true
    // The key only in a header: in the query string it would end up in logs.
    subscriptionKeyParameterNames: {
      header: 'Ocp-Apim-Subscription-Key'
      query: ''
    }
  }
}

resource submitTransportOrder 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  parent: partnerApi
  name: 'submit-transport-order'
  properties: {
    displayName: 'Submit Transport Order'
    description: 'Book a Transport Order as a Shipper (contracts/partner-api).'
    method: 'POST'
    urlTemplate: '/transport-orders'
  }
}

resource submitTransportOrderPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = {
  parent: submitTransportOrder
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('partner-api/submit-transport-order.policy.xml')
  }
  dependsOn: [
    policyValues
    intakeLogicApp
  ]
}

resource answerCarrierBooking 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  parent: partnerApi
  name: 'answer-carrier-booking'
  properties: {
    displayName: 'Answer Carrier booking'
    description: 'Confirm or reject a Carrier booking request as a Carrier (contracts/partner-api).'
    method: 'POST'
    urlTemplate: '/carrier-answers'
  }
}

resource answerCarrierBookingPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = {
  parent: answerCarrierBooking
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('partner-api/answer-carrier-booking.policy.xml')
  }
  dependsOn: [
    policyValues
    answerLogicApp
  ]
}

// A Carrier's tracking message is accepted at the edge and put on the tracking queue (ADR 0008). No Logic App is called: the
// Carrier-tracking Logic App reads the queue.
resource submitCarrierTracking 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  parent: partnerApi
  name: 'submit-carrier-tracking'
  properties: {
    displayName: 'Submit Carrier tracking'
    description: 'Report what happened to a Shipment as a Carrier. Accepted with 202 and processed later (contracts/partner-api).'
    method: 'POST'
    urlTemplate: '/carrier-tracking'
  }
}

resource submitCarrierTrackingPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = {
  parent: submitCarrierTracking
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('partner-api/submit-carrier-tracking.policy.xml')
  }
  dependsOn: [
    policyValues
  ]
}

// A Carrier's Proof of delivery is accepted at the edge by claim check (ADR 0009): API Management stores the body unparsed in the
// inbound container and puts only the blob's reference on the Proof of delivery queue. No Logic App is called: the Carrier Proof of
// delivery Logic App reads the queue.
resource submitCarrierProofOfDelivery 'Microsoft.ApiManagement/service/apis/operations@2024-05-01' = {
  parent: partnerApi
  name: 'submit-carrier-proof-of-delivery'
  properties: {
    displayName: 'Submit Carrier Proof of delivery'
    description: 'Prove a delivery as a Carrier, with the files. At most 25 MB; accepted with 202 and processed later (contracts/partner-api).'
    method: 'POST'
    urlTemplate: '/carrier-proofs-of-delivery'
  }
}

resource submitCarrierProofOfDeliveryPolicy 'Microsoft.ApiManagement/service/apis/operations/policies@2024-05-01' = {
  parent: submitCarrierProofOfDelivery
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('partner-api/submit-carrier-proof-of-delivery.policy.xml')
  }
  dependsOn: [
    policyValues
  ]
}

// The subscription key names the product and rates the partner; the token says who the partner is (ADR 0005).
resource partners 'Microsoft.ApiManagement/service/products@2024-05-01' = {
  parent: apiManagement
  name: 'norvia-partners'
  properties: {
    displayName: 'Norvia partners'
    description: 'Shippers and Carriers who book and report through the Norvia Partner API.'
    subscriptionRequired: true
    approvalRequired: false
    state: 'published'
  }
}

resource partnersUseThePartnerApi 'Microsoft.ApiManagement/service/products/apis@2024-05-01' = {
  parent: partners
  name: partnerApi.name
}

// One subscription per Persona, named after it. Its keys stay in API Management; whoever needs one lists it.
resource personaSubscriptions 'Microsoft.ApiManagement/service/subscriptions@2024-05-01' = [
  for persona in personaClientIds: {
    parent: apiManagement
    name: split(persona, '=')[0]
    properties: {
      displayName: 'Persona ${split(persona, '=')[0]}'
      scope: partners.id
      state: 'active'
    }
  }
]

// The Partner portal books as each Persona with its subscription key (ADR 0005), which it reads from here with its own
// identity. It also reads the credential each Carrier Persona's booking endpoint asks of its caller. Nothing else may
// read the subscription keys; the P4 booking Logic App reads only the Carrier endpoint credentials.
resource partnerPortalSecrets 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: 'kv-portal-${resourceToken}'
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
  }
}

resource subscriptionKeys 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = [
  for (persona, i) in personaClientIds: {
    parent: partnerPortalSecrets
    name: 'subscription-key-${split(persona, '=')[0]}'
    properties: {
      value: personaSubscriptions[i].listSecrets().primaryKey
    }
  }
]

// The Carrier Personas are the seed file's, as in identities.bicep. Each one's booking endpoint in the Partner portal has
// a credential of its own, named after the Carrier's ID at Norvia, which is what the endpoint's address names.
var carrierIds = map(
  filter(loadJsonContent('../../seed/norvia.seed.json').personas, persona => persona.party.type == 'Carrier'),
  persona => persona.party.id
)

// Derived from the seed and the Carrier's ID, as 64 hex digits from two GUIDs. Nobody reads the seed back out of the
// vault, and only the portal and the booking Logic App read these.
resource carrierEndpointKeys 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = [
  for carrierId in carrierIds: {
    parent: partnerPortalSecrets
    name: 'carrier-endpoint-key-${carrierId}'
    properties: {
      value: replace('${guid(carrierEndpointKeySeed, carrierId, 'carrier-endpoint-key')}${guid(carrierId, carrierEndpointKeySeed, 'carrier-endpoint-key')}', '-', '')
    }
  }
]

// Every Shipper Persona has a notification endpoint in the Partner portal (P7), with a credential of its own named after the
// Shipper's ID, derived like the Carriers'. The portal reads them; the Shipper notification Logic App gets the role to read
// them below.
var shipperIds = map(
  filter(loadJsonContent('../../seed/norvia.seed.json').personas, persona => persona.party.type == 'Shipper'),
  persona => persona.party.id
)

resource shipperEndpointKeys 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = [
  for shipperId in shipperIds: {
    parent: partnerPortalSecrets
    name: 'shipper-endpoint-key-${shipperId}'
    properties: {
      value: replace('${guid(carrierEndpointKeySeed, shipperId, 'shipper-endpoint-key')}${guid(shipperId, carrierEndpointKeySeed, 'shipper-endpoint-key')}', '-', '')
    }
  }
]

var keyVaultSecretsUser = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource partnerPortalReadsTheKeys 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(partnerPortalSecrets.id, partnerPortalPrincipalId, keyVaultSecretsUser)
  scope: partnerPortalSecrets
  properties: {
    principalId: partnerPortalPrincipalId
    roleDefinitionId: keyVaultSecretsUser
    principalType: 'ServicePrincipal'
  }
}

// Scoped to each secret, so the booking Logic App can't read a Persona's subscription key.
resource bookingReadsTheCarrierEndpointKeys 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (carrierId, i) in carrierIds: {
    name: guid(carrierEndpointKeys[i].id, bookingPrincipalId, keyVaultSecretsUser)
    scope: carrierEndpointKeys[i]
    properties: {
      principalId: bookingPrincipalId
      roleDefinitionId: keyVaultSecretsUser
      principalType: 'ServicePrincipal'
    }
  }
]

// Scoped to each secret, so the Shipper notification Logic App can't read a Persona's subscription key or a Carrier's credential.
resource shipperNotificationReadsTheShipperEndpointKeys 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (shipperId, i) in shipperIds: {
    name: guid(shipperEndpointKeys[i].id, shipperNotificationPrincipalId, keyVaultSecretsUser)
    scope: shipperEndpointKeys[i]
    properties: {
      principalId: shipperNotificationPrincipalId
      roleDefinitionId: keyVaultSecretsUser
      principalType: 'ServicePrincipal'
    }
  }
]

output apiManagementName string = apiManagement.name
output partnerPortalKeyVaultName string = partnerPortalSecrets.name
output partnerApiUrl string = '${apiManagement.properties.gatewayUrl}/${partnerApi.properties.path}'
