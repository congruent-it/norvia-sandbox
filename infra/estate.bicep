// The AIS estate (ADR 0004). azd's preprovision hook deploys it before Aspire provisions the apps into the same
// resource group; the apps reference what it owns instead of creating it.
targetScope = 'subscription'

@description('The azd environment. The estate and the apps share the resource group rg-<environmentName>.')
@minLength(1)
@maxLength(64)
param environmentName string

@description('The Azure region for every resource in the estate.')
param location string

@description('''How many days the integration layer keeps what it traced: Application Insights, Log Analytics and Logic Apps
run history now (at most 365 days for run history), and the transit containers once they exist. The one place retention
is set (coding guidelines §7). Application Insights accepts only these values.''')
@allowed([30, 60, 90, 120, 180, 270, 365, 550, 730])
param integrationRetentionDays int = 90

@description('''The presenter's Entra object ID, empty for none. The presenter may read the estate's messages, for example to see
an event land on its topic, and is the one user who may open the demo UIs in Azure.''')
param presenterPrincipalId string = ''

@description('''The address the estate's alerts are emailed to, empty for none (ESTATE_ALERT_EMAIL).''')
param alertEmail string = ''

@description('''The as-found toggles, in one object (docs/operations/as-found.md). The presets in infra/presets are values of it.
Each list names the flows or queues that have no alert, and an alert not built is absent, never disabled. The default is
the fixed state: every alert, and a receiver.''')
param asFound object = {
  flowsWithoutFailedRunAlert: []
  entitiesWithoutDeadLetterAlert: []
  noAlertReceiver: false
}

@description('''Easy Auth's sign-in callbacks on the demo UIs (the TMS UI and the Partner portal), comma separated, empty before
the apps are first deployed. azd's postdeploy hook stores them, so the demo UIs' app registration keeps them when the
estate is deployed again.''')
param demoUiRedirectUris string = ''

@description('''Easy Auth's sign-in callback on the Integration console, empty before the console is first deployed. azd's
postdeploy hook stores it, so the console's app registration keeps it when the estate is deployed again.''')
param integrationConsoleRedirectUris string = ''

@description('''Easy Auth's sign-in callback on the Driver app, empty before the Driver app is first deployed. azd's postdeploy
hook stores it, so the Driver app's app registration keeps it when the estate is deployed again.''')
param driverAppRedirectUris string = ''

@description('''Easy Auth's sign-in callback on the Simulator, empty before the Simulator is first deployed. azd's postdeploy hook stores
it, so the Simulator's app registration keeps it when the estate is deployed again.''')
param simulatorRedirectUris string = ''

@description('''Easy Auth's sign-in callback on the ERP, empty before the ERP is first deployed. azd's postdeploy hook stores it, so the
ERP's app registration keeps it when the estate is deployed again.''')
param erpRedirectUris string = ''

@description('''Whether the Simulator does anything (Simulator:Enabled). The estate decides: switch it off to make the deployed Simulator answer
every request but its health checks with 404, without removing it. The Simulator sends orders into the estate, so it is a demo tool.''')
param simulatorEnabled bool = true

@description('''The most orders one Simulation run may send (Simulator:MaxOrdersPerRun). A deployed estate has API Management on the Developer tier
(ADR 0021) and Logic Apps that bill by action, so the cap is lower than the Simulator's own 1,000.''')
@minValue(1)
@maxValue(1000)
param simulatorMaxOrdersPerRun int = 200

@description('The most requests a Simulation run has in flight to the estate at once (Simulator:MaxConcurrentRequests).')
@minValue(1)
@maxValue(16)
param simulatorMaxConcurrentRequests int = 4

@description('''The TMS API's address, empty before the apps are first deployed. azd's postdeploy hook stores it, so the intake,
Carrier-answer, Shipper notification, Carrier cancellation, Trip dispatch, Carrier-tracking and Carrier Proof of delivery Logic Apps keep it when the estate is deployed again.''')
param tmsBaseUrl string = ''

@secure()
@description('''A random value the Carrier endpoints' credentials are derived from. The deployment hook (provision-estate.sh) makes it
once and keeps it in the azd environment.''')
param carrierEndpointKeySeed string

@description('''The Partner portal's address, empty before the apps are first deployed. azd's postdeploy hook stores it, so the P4
booking, Shipper notification and Carrier cancellation Logic Apps keep it when the estate is deployed again.''')
param partnerPortalBaseUrl string = ''

@description('''The Driver app API's address, empty before the apps are first deployed. azd's postdeploy hook stores it, so the Trip dispatch
Logic App keeps it when the estate is deployed again.''')
param driverAppBaseUrl string = ''

@description('''The ERP billing API's address, empty before the apps are first deployed. azd's postdeploy hook stores it, so the Billing,
Carrier cost and Carrier invoice Logic Apps keep it when the estate is deployed again.''')
param erpBaseUrl string = ''

var tags = {
  'azd-env-name': environmentName
}

// Aspire provisions the apps into rg-<environmentName> with the same tags.
resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module monitoring 'estate/monitoring.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
    alertEmail: alertEmail
    noAlertReceiver: asFound.noAlertReceiver
  }
}

module alerts 'estate/alerts.bicep' = {
  scope: resourceGroup
  params: {
    tags: tags
    actionGroupId: monitoring.outputs.actionGroupId
    asFound: asFound
    serviceBusName: messaging.outputs.namespaceName
    flows: [
      { name: 'transport-order-intake', logicAppName: intakeLogicApp.outputs.logicAppName, workflowName: 'transport-order-intake' }
      { name: 'carrier-answer', logicAppName: answerLogicApp.outputs.logicAppName, workflowName: 'carrier-answer' }
      { name: 'carrier-invoice', logicAppName: carrierInvoiceLogicApp.outputs.logicAppName, workflowName: 'carrier-invoice' }
      { name: 'carrier-booking', logicAppName: bookingLogicApp.outputs.logicAppName, workflowName: 'carrier-booking' }
      { name: 'shipper-notification', logicAppName: shipperNotificationLogicApp.outputs.logicAppName, workflowName: 'shipper-notification' }
      { name: 'carrier-cancellation', logicAppName: carrierCancellationLogicApp.outputs.logicAppName, workflowName: 'carrier-cancellation' }
      { name: 'trip-dispatch', logicAppName: tripDispatchLogicApp.outputs.logicAppName, workflowName: 'trip-dispatch' }
      { name: 'driver-reports', logicAppName: driverReportsLogicApp.outputs.logicAppName, workflowName: 'driver-reports' }
      { name: 'billing', logicAppName: billingLogicApp.outputs.logicAppName, workflowName: 'billing' }
      { name: 'carrier-cost', logicAppName: carrierCostLogicApp.outputs.logicAppName, workflowName: 'carrier-cost' }
      { name: 'shipper-invoice', logicAppName: shipperInvoiceLogicApp.outputs.logicAppName, workflowName: 'shipper-invoice' }
      { name: 'carrier-invoice-outcome', logicAppName: carrierInvoiceOutcomeLogicApp.outputs.logicAppName, workflowName: 'carrier-invoice-outcome' }
      { name: 'carrier-tracking', logicAppName: trackingLogicApp.outputs.logicAppName, workflowName: 'carrier-tracking' }
      { name: 'carrier-proof-of-delivery', logicAppName: proofOfDeliveryLogicApp.outputs.logicAppName, workflowName: 'carrier-proof-of-delivery' }
    ]
    // Every queue and every subscription a Logic App reads, by the name Service Bus reports it under. The monitoring
    // subscriptions are left out on purpose (infra/estate/alert-exemptions.json).
    entities: [
      'carrier-tracking'
      'carrier-proof-of-delivery'
      'carrier-booking'
      'shipper-notification'
      'carrier-cancellation'
      'carrier-cost'
      'trip-dispatch'
      'driver-reports'
      'billing'
      'shipper-invoice'
      'carrier-invoice-outcome'
    ]
  }
}

module messaging 'estate/messaging.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    presenterPrincipalId: presenterPrincipalId
    bookingPrincipalId: identities.outputs.bookingPrincipalId
    shipperNotificationPrincipalId: identities.outputs.shipperNotificationPrincipalId
    carrierCancellationPrincipalId: identities.outputs.carrierCancellationPrincipalId
    tripDispatchPrincipalId: identities.outputs.tripDispatchPrincipalId
    driverReportsPrincipalId: identities.outputs.driverReportsPrincipalId
    billingPrincipalId: identities.outputs.billingPrincipalId
    carrierCostIdentityName: identities.outputs.carrierCostIdentityName
    shipperInvoicePrincipalId: identities.outputs.shipperInvoice.principalId
    carrierInvoiceOutcomePrincipalId: identities.outputs.carrierInvoiceOutcome.principalId
    driverAppPrincipalId: identities.outputs.driverAppPrincipalId
    trackingPrincipalId: identities.outputs.trackingPrincipalId
    proofOfDeliveryPrincipalId: identities.outputs.proofOfDeliveryPrincipalId
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module identities 'estate/identities.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    presenterPrincipalId: presenterPrincipalId
    demoUiRedirectUris: demoUiRedirectUris
    integrationConsoleRedirectUris: integrationConsoleRedirectUris
    driverAppRedirectUris: driverAppRedirectUris
    simulatorRedirectUris: simulatorRedirectUris
    erpRedirectUris: erpRedirectUris
  }
}

module intakeLogicApp 'estate/intake-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    intakeIdentityName: identities.outputs.intakeIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    apiManagementClientId: identities.outputs.apiManagementClientId
    intakeTriggerClientId: identities.outputs.intakeTriggerClientId
    intakeTriggerAudience: identities.outputs.intakeTriggerAudience
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module answerLogicApp 'estate/answer-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    answerIdentityName: identities.outputs.answerIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    apiManagementClientId: identities.outputs.apiManagementClientId
    answerTriggerClientId: identities.outputs.answerTrigger.clientId
    answerTriggerAudience: identities.outputs.answerTrigger.audience
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module carrierInvoiceLogicApp 'estate/carrier-invoice-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    carrierInvoiceIdentityName: identities.outputs.carrierInvoice.identityName
    erpApiClientId: identities.outputs.erpApiClientId
    erpBaseUrl: erpBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    apiManagementClientId: identities.outputs.apiManagementClientId
    carrierInvoiceTriggerClientId: identities.outputs.carrierInvoice.triggerClientId
    carrierInvoiceTriggerAudience: identities.outputs.carrierInvoice.triggerAudience
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module bookingLogicApp 'estate/booking-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    bookingIdentityName: identities.outputs.bookingIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    partnerPortalBaseUrl: partnerPortalBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    carrierKeyVaultName: partnerApi.outputs.partnerPortalKeyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module shipperNotificationLogicApp 'estate/shipper-notification-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    shipperNotificationIdentityName: identities.outputs.shipperNotificationIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    partnerPortalBaseUrl: partnerPortalBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    shipperKeyVaultName: partnerApi.outputs.partnerPortalKeyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module carrierCancellationLogicApp 'estate/carrier-cancellation-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    carrierCancellationIdentityName: identities.outputs.carrierCancellationIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    partnerPortalBaseUrl: partnerPortalBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    carrierKeyVaultName: partnerApi.outputs.partnerPortalKeyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module driverApp 'estate/driver-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    endpointKeySeed: carrierEndpointKeySeed
    integrationRetentionDays: integrationRetentionDays
    driverAppPrincipalId: identities.outputs.driverAppPrincipalId
    driverReportsPrincipalId: identities.outputs.driverReportsPrincipalId
    tripDispatchPrincipalId: identities.outputs.tripDispatchPrincipalId
  }
}

module tripDispatchLogicApp 'estate/trip-dispatch-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    tripDispatchIdentityName: identities.outputs.tripDispatchIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    driverAppBaseUrl: driverAppBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    driverAppKeyVaultName: driverApp.outputs.keyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module driverReportsLogicApp 'estate/driver-reports-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    driverReportsIdentityName: identities.outputs.driverReportsIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    driverAppBaseUrl: driverAppBaseUrl
    driverAppApiClientId: identities.outputs.driverAppApiClientId
    driverProofsContainerName: driverApp.outputs.containerName
    driverProofsBlobEndpoint: driverApp.outputs.blobEndpoint
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module billingLogicApp 'estate/billing-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    billingIdentityName: identities.outputs.billingIdentityName
    erpApiClientId: identities.outputs.erpApiClientId
    erpBaseUrl: erpBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module carrierCostLogicApp 'estate/carrier-cost-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    carrierCostIdentityName: identities.outputs.carrierCostIdentityName
    erpApiClientId: identities.outputs.erpApiClientId
    erpBaseUrl: erpBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module shipperInvoiceLogicApp 'estate/shipper-invoice-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    shipperInvoiceIdentityName: identities.outputs.shipperInvoice.identityName
    erpApiClientId: identities.outputs.erpApiClientId
    erpBaseUrl: erpBaseUrl
    partnerPortalBaseUrl: partnerPortalBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    shipperKeyVaultName: partnerApi.outputs.partnerPortalKeyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module carrierInvoiceOutcomeLogicApp 'estate/carrier-invoice-outcome-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    carrierInvoiceOutcomeIdentityName: identities.outputs.carrierInvoiceOutcome.identityName
    erpApiClientId: identities.outputs.erpApiClientId
    erpBaseUrl: erpBaseUrl
    partnerPortalBaseUrl: partnerPortalBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    carrierKeyVaultName: partnerApi.outputs.partnerPortalKeyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module trackingLogicApp 'estate/tracking-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    trackingIdentityName: identities.outputs.trackingIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module inboundStorage 'estate/inbound-storage.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    proofOfDeliveryPrincipalId: identities.outputs.proofOfDeliveryPrincipalId
    presenterPrincipalId: presenterPrincipalId
  }
}

module proofOfDeliveryLogicApp 'estate/proof-of-delivery-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    proofOfDeliveryIdentityName: identities.outputs.proofOfDeliveryIdentityName
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    serviceBusName: messaging.outputs.namespaceName
    inboundContainerName: inboundStorage.outputs.containerName
    inboundBlobEndpoint: inboundStorage.outputs.blobEndpoint
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
  }
}

module partnerApi 'estate/partner-api.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    apiManagementIdentityName: identities.outputs.apiManagementIdentityName
    partnerApiClientId: identities.outputs.partnerApiClientId
    tmsApiClientId: identities.outputs.tmsApiClientId
    tmsBaseUrl: tmsBaseUrl
    shippersByClientId: join(filter(identities.outputs.shipperByClientId, entry => !empty(entry)), ';')
    carriersByClientId: join(filter(identities.outputs.carrierByClientId, entry => !empty(entry)), ';')
    personaClientIds: identities.outputs.personaClientIds
    intakeLogicAppHostName: intakeLogicApp.outputs.logicAppHostName
    intakeTriggerAudience: identities.outputs.intakeTriggerAudience
    answerLogicAppHostName: answerLogicApp.outputs.logicAppHostName
    answerTriggerAudience: identities.outputs.answerTrigger.audience
    carrierInvoiceLogicAppHostName: carrierInvoiceLogicApp.outputs.logicAppHostName
    carrierInvoiceTriggerAudience: identities.outputs.carrierInvoice.triggerAudience
    serviceBusName: messaging.outputs.namespaceName
    trackingQueueName: messaging.outputs.queueName
    proofOfDeliveryQueueName: messaging.outputs.proofOfDeliveryQueueName
    inboundStorageAccountName: inboundStorage.outputs.storageAccountName
    inboundContainerName: inboundStorage.outputs.containerName
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    partnerPortalPrincipalId: identities.outputs.partnerPortalPrincipalId
    simulatorPrincipalId: identities.outputs.simulatorPrincipalId
    bookingPrincipalId: identities.outputs.bookingPrincipalId
    shipperNotificationPrincipalId: identities.outputs.shipperNotificationPrincipalId
    shipperInvoicePrincipalId: identities.outputs.shipperInvoice.principalId
    carrierInvoiceOutcomePrincipalId: identities.outputs.carrierInvoiceOutcome.principalId
    carrierCancellationPrincipalId: identities.outputs.carrierCancellationPrincipalId
    carrierEndpointKeySeed: carrierEndpointKeySeed
  }
}

// The hook stores each output as AZURE_<NAME>, which is where azd looks up the AppHost's Estate parameters.
output ESTATE_SERVICE_BUS_NAMESPACE string = messaging.outputs.namespaceName
output ESTATE_LOG_ANALYTICS_WORKSPACE string = monitoring.outputs.workspaceName
output ESTATE_LOG_ANALYTICS_WORKSPACE_ID string = monitoring.outputs.workspaceId
// The Integration console caps its searches at it: nothing older is kept.
output ESTATE_INTEGRATION_RETENTION_DAYS string = string(integrationRetentionDays)
// Where the Integration console reads the Logic Apps' run history.
output ESTATE_RESOURCE_GROUP_ID string = resourceGroup.id
output ESTATE_APPLICATION_INSIGHTS string = monitoring.outputs.applicationInsightsName
output ESTATE_ENTRA_ISSUER string = identities.outputs.entraIssuer
output ESTATE_ENTRA_TENANT_ID string = identities.outputs.entraTenantId
output ESTATE_TMS_API_CLIENT_ID string = identities.outputs.tmsApiClientId
output ESTATE_INTAKE_CLIENT_ID string = identities.outputs.intakeClientId
output ESTATE_INTAKE_IDENTITY_ID string = identities.outputs.intakeIdentityId
output ESTATE_SHIPPER_NOTIFICATION_IDENTITY_NAME string = identities.outputs.shipperNotificationIdentityName
output ESTATE_SHIPPER_NOTIFICATION_CLIENT_ID string = identities.outputs.shipperNotificationClientId
output ESTATE_CARRIER_CANCELLATION_IDENTITY_NAME string = identities.outputs.carrierCancellationIdentityName
output ESTATE_CARRIER_CANCELLATION_CLIENT_ID string = identities.outputs.carrierCancellationClientId
output ESTATE_TRIP_DISPATCH_IDENTITY_NAME string = identities.outputs.tripDispatchIdentityName
output ESTATE_DRIVER_REPORTS_IDENTITY_NAME string = identities.outputs.driverReportsIdentityName
output ESTATE_DRIVER_APP_API_CLIENT_ID string = identities.outputs.driverAppApiClientId
output ESTATE_DRIVER_APP_IDENTITY_NAME string = identities.outputs.driverAppIdentityName
output ESTATE_DRIVER_APP_CLIENT_ID string = identities.outputs.driverAppClientId
output ESTATE_DRIVER_APP_KEY_VAULT string = driverApp.outputs.keyVaultName
output ESTATE_DRIVER_APP_BLOB_ENDPOINT string = driverApp.outputs.blobEndpoint
output ESTATE_TMS_UI_IDENTITY_NAME string = identities.outputs.tmsUiIdentityName
output ESTATE_TMS_UI_CLIENT_ID string = identities.outputs.tmsUiClientId
output ESTATE_INTAKE_LOGIC_APP string = intakeLogicApp.outputs.logicAppName
output ESTATE_BOOKING_LOGIC_APP string = bookingLogicApp.outputs.logicAppName
output ESTATE_SHIPPER_NOTIFICATION_LOGIC_APP string = shipperNotificationLogicApp.outputs.logicAppName
output ESTATE_CARRIER_CANCELLATION_LOGIC_APP string = carrierCancellationLogicApp.outputs.logicAppName
output ESTATE_TRIP_DISPATCH_LOGIC_APP string = tripDispatchLogicApp.outputs.logicAppName
output ESTATE_DRIVER_REPORTS_LOGIC_APP string = driverReportsLogicApp.outputs.logicAppName
output ESTATE_BILLING_LOGIC_APP string = billingLogicApp.outputs.logicAppName
output ESTATE_CARRIER_COST_LOGIC_APP string = carrierCostLogicApp.outputs.logicAppName
output ESTATE_SHIPPER_INVOICE_LOGIC_APP string = shipperInvoiceLogicApp.outputs.logicAppName
output ESTATE_ANSWER_LOGIC_APP string = answerLogicApp.outputs.logicAppName
output ESTATE_TRACKING_LOGIC_APP string = trackingLogicApp.outputs.logicAppName
output ESTATE_TRACKING_CLIENT_ID string = identities.outputs.trackingClientId
output ESTATE_TRACKING_IDENTITY_NAME string = identities.outputs.trackingIdentityName
output ESTATE_PROOF_OF_DELIVERY_LOGIC_APP string = proofOfDeliveryLogicApp.outputs.logicAppName
output ESTATE_PROOF_OF_DELIVERY_CLIENT_ID string = identities.outputs.proofOfDeliveryClientId
output ESTATE_PROOF_OF_DELIVERY_IDENTITY_NAME string = identities.outputs.proofOfDeliveryIdentityName
output ESTATE_INBOUND_STORAGE_ACCOUNT string = inboundStorage.outputs.storageAccountName
output ESTATE_INBOUND_CONTAINER string = inboundStorage.outputs.containerName
output ESTATE_API_MANAGEMENT string = partnerApi.outputs.apiManagementName
output ESTATE_PARTNER_API_URL string = partnerApi.outputs.partnerApiUrl
output ESTATE_PARTNER_API_CLIENT_ID string = identities.outputs.partnerApiClientId
output ESTATE_PERSONA_CLIENT_IDS string = join(identities.outputs.personaClientIds, ';')
output ESTATE_PARTNER_PORTAL_IDENTITY_ID string = identities.outputs.partnerPortalIdentityId
output ESTATE_PARTNER_PORTAL_CLIENT_ID string = identities.outputs.partnerPortalClientId
output ESTATE_PARTNER_PORTAL_IDENTITY_NAME string = identities.outputs.partnerPortalIdentityName
output ESTATE_PARTNER_PORTAL_KEY_VAULT string = partnerApi.outputs.partnerPortalKeyVaultName
output ESTATE_DEMO_UIS_CLIENT_ID string = identities.outputs.demoUisClientId
output ESTATE_INTEGRATION_CONSOLE_CLIENT_ID string = identities.outputs.integrationConsoleClientId
output ESTATE_DRIVER_APP_REGISTRATION_CLIENT_ID string = identities.outputs.driverAppRegistrationClientId
output ESTATE_INTEGRATION_CONSOLE_IDENTITY_NAME string = identities.outputs.integrationConsoleIdentityName
output ESTATE_SIMULATOR_IDENTITY_NAME string = identities.outputs.simulatorIdentityName
output ESTATE_SIMULATOR_CLIENT_ID string = identities.outputs.simulatorClientId
output ESTATE_SIMULATOR_REGISTRATION_CLIENT_ID string = identities.outputs.simulatorRegistrationClientId
output ESTATE_DRIVER_CLIENT_IDS string = join(identities.outputs.driverClientIds, ';')
output ESTATE_ERP_IDENTITY_NAME string = identities.outputs.erpIdentityName
output ESTATE_ERP_REGISTRATION_CLIENT_ID string = identities.outputs.erpRegistrationClientId
output ESTATE_ERP_API_CLIENT_ID string = identities.outputs.erpApiClientId
output ESTATE_SIMULATOR_ENABLED string = string(simulatorEnabled)
output ESTATE_SIMULATOR_MAX_ORDERS_PER_RUN string = string(simulatorMaxOrdersPerRun)
output ESTATE_SIMULATOR_MAX_CONCURRENT_REQUESTS string = string(simulatorMaxConcurrentRequests)
