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
  noInboundLifecycleRule: false
  noTransitLifecycleRule: false
  proofFilesStayHot: false
  noMalwareScanning: false
  carrierSecretInlined: false
  appInsightsRetentionShort: false
  logicAppsWithoutTrackedProperties: []
  staleThresholdsUnset: false
  proofMissingThresholdUnset: false
  lateDeliveryCheckOff: false
  shipperTrackingCached: false
  noShipperRateLimit: false
  noReportBackToDriverApp: false
  noConversationSweeper: false
}

@description('''Easy Auth's sign-in callbacks on the demo UIs (the TMS UI and the Partner portal), comma separated, empty before
the apps are first deployed. azd's postdeploy hook stores them, so the demo UIs' app registration keeps them when the
estate is deployed again.''')
param demoUiRedirectUris string = ''

@description('''Easy Auth's sign-in callback on the Integration console, empty before the console is first deployed. azd's
postdeploy hook stores it, so the console's app registration keeps it when the estate is deployed again.''')
param integrationConsoleRedirectUris string = ''

@description('''The Integration console's address, empty before the console is first deployed. infra/deploy-agents.ps1 stores it, so the
Integration operations agent's API Management backend keeps it when the estate is deployed again.''')
param integrationConsoleBaseUrl string = ''

@description('''Easy Auth's sign-in callback on the Estate health check, empty before the app is first deployed. infra/register-sign-in-callbacks.ps1 stores it.''')
param healthCheckRedirectUris string = ''

@description('''The Estate health check's address, empty before the app is first deployed. infra/deploy-agents.ps1 stores it (ESTATE_HEALTH_CHECK_BASE_URL), so
the Estate health-check agent's API Management backend keeps it when the estate is deployed again.''')
param healthCheckBaseUrl string = ''

@description('''The Booking draft host's address, empty before the host is first deployed. infra/deploy-agents.ps1 stores it, so the Booking draft
backend keeps it when the estate is deployed again.''')
param bookingDraftBaseUrl string = ''

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
@description('''A random value the Carrier endpoints' credentials are derived from. The deployment hook (provision-estate.ps1) makes it
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

@description('''Whether the Agents do anything (Agents:Enabled), off by default. The estate decides, as it does for the Simulator. The Foundry
account, project and model exist either way.''')
param agentsEnabled bool = false

@description('''The Azure region of the Foundry account. Empty is AZURE_LOCATION when the model has a DataZoneStandard deployment there, and
the nearest EU region that has it (Sweden Central) when not (docs/operations/foundry.md).''')
param foundryLocation string = ''

@description('The model every Agent shares. The default is the small model of docs/operations/foundry.md, spike 1.')
param foundryModelName string = 'gpt-5.4-mini'

@description('The shared model\'s version.')
param foundryModelVersion string = '2026-03-17'

@description('''Whether the shared model deployment exists. Without it every Agent run fails, which is how an agent toggle (#377) breaks
the Agents.''')
param foundryModelDeployed bool = true

@description('The cap on the shared model deployment, in thousands of tokens per minute (DataZoneStandard).')
@minValue(1)
@maxValue(100)
param foundryModelCapacity int = 30

@description('''Entra object IDs besides the presenter that may use the Foundry project, such as the pipeline that deploys the Agents. Each gets the
Foundry User role.''')
param foundryUserPrincipalIds array = []

var tags = {
  'azd-env-name': environmentName
}

// EU regions that have a DataZoneStandard deployment of gpt-5.4-mini (Microsoft Learn, October 2026).
var foundryRegions = ['francecentral', 'germanywestcentral', 'italynorth', 'norwayeast', 'polandcentral', 'spaincentral', 'swedencentral', 'switzerlandnorth', 'westeurope']

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
    appInsightsRetentionShort: asFound.?appInsightsRetentionShort ?? false
  }
}

// The Estate health check's identity, with Reader on this resource group, and the app registration its operators sign in to (#392). It has
// no outputs of the template, bar the identity's principal: the estate is at the 64 a template may have, so the scripts and the AppHost
// find the rest by name.
module healthCheck 'estate/health-check.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    presenterPrincipalId: presenterPrincipalId
    healthCheckRedirectUris: healthCheckRedirectUris
  }
}

// The TMS UI's identity (infra/estate/identities.bicep), by its name: identities.bicep and this template are both at the 64 outputs a
// template may have, so its principal is read from the identity itself, which the foundry module waits for.
resource tmsUiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  scope: resourceGroup
  name: 'id-tms-ui-${uniqueString(resourceGroup.id)}'
  dependsOn: [
    identities
  ]
}

// The ERP's identity (infra/estate/identities.bicep), by its name, for the same reason: the ERP UI asks its Agent as it.
resource erpIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  scope: resourceGroup
  name: 'id-erp-${uniqueString(resourceGroup.id)}'
  dependsOn: [
    identities
  ]
}

module foundry 'estate/foundry.bicep' = {
  scope: resourceGroup
  params: {
    location: !empty(foundryLocation) ? foundryLocation : contains(foundryRegions, location) ? location : 'swedencentral'
    tags: tags
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    modelName: foundryModelName
    modelVersion: foundryModelVersion
    modelDeployed: foundryModelDeployed
    modelCapacity: foundryModelCapacity
    agentsEnabled: agentsEnabled
    foundryUserPrincipalIds: concat(empty(presenterPrincipalId) ? [] : [presenterPrincipalId], foundryUserPrincipalIds)
    // The Integration console asks its Agent as its own identity (Explain), and so does the Estate health check, the TMS UI (Explain on a Rejected booking), the ERP UI (Explain on a Carrier invoice line) and the Simulator (its plans' pools of Carrier texts).
    applicationPrincipalIds: [identities.outputs.integrationConsolePrincipalId, healthCheck.outputs.principalId, tmsUiIdentity.properties.principalId, erpIdentity.properties.principalId, identities.outputs.simulatorPrincipalId]
    // The Booking draft host asks the Intake agent as its own identity, in the Intake project only, and so does the Partner portal for the Shipper tracking
    // assistant (ADR 0049): a Shipper's question is text of the same kind, so its Agent is published there too, where nothing is traced server-side.
    intakeApplicationPrincipalIds: [bookingDraft.outputs.principalId, identities.outputs.partnerPortalPrincipalId]
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
      { name: 'conversation-sweeper', logicAppName: conversationSweeperLogicApp.outputs.logicAppName, workflowName: 'conversation-sweeper' }
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
    healthCheckPrincipalId: healthCheck.outputs.principalId
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
    carrierSecretInlined: asFound.?carrierSecretInlined ?? false
    carrierEndpointKeySeed: carrierEndpointKeySeed
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
    noLifecycleRule: asFound.?noTransitLifecycleRule ?? false
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

module logicAppSwitchRole 'estate/logic-app-switch-role.bicep' = {
  scope: resourceGroup
  params: {
    assignableScope: resourceGroup.id
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
    partnerPortalPrincipalId: identities.outputs.partnerPortalPrincipalId
    logicAppSwitchRoleId: logicAppSwitchRole.outputs.roleDefinitionId
  }
}

module conversationSweeperLogicApp 'estate/conversation-sweeper-logic-app.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    integrationRetentionDays: integrationRetentionDays
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    planName: intakeLogicApp.outputs.planName
    contentKeyVaultName: intakeLogicApp.outputs.keyVaultName
    integrationConsolePrincipalId: identities.outputs.integrationConsolePrincipalId
    foundryAccountName: foundry.outputs.accountName
    foundryProjectName: foundry.outputs.projectName
    foundryProjectEndpoint: foundry.outputs.projectEndpoint
    enabled: !(asFound.?noConversationSweeper ?? false)
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
    noLifecycleRule: asFound.?noInboundLifecycleRule ?? false
    noMalwareScanning: asFound.?noMalwareScanning ?? false
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
    shipperTrackingCached: asFound.?shipperTrackingCached ?? false
    noShipperRateLimit: asFound.?noShipperRateLimit ?? false
  }
}

// The example MCP server and its identities (infra/smoke-test-agent-mcp-server.ps1). The estate is at the 64 outputs a template may have,
// so the smoke run finds the identities by their app registrations' names and the address by the API Management service.
module agentMcpExample 'estate/agent-mcp-example.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
  }
}

// The Integration operations agent's MCP server over the console's agent API, with the two app registrations that keep it narrow
// (infra/smoke-test-integration-operations-agent.ps1). Like the example, it has no outputs: the estate is at the 64 a template may have,
// so the scripts and the AppHost find its app registrations by their names.
module integrationOperationsMcp 'estate/integration-operations-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    apiManagementClientId: identities.outputs.apiManagementClientId
    consoleBaseUrl: integrationConsoleBaseUrl
  }
  // API Management refuses a write to a service that another write has just changed (PreconditionFailed: "Resource was modified
  // since last retrieval"), so the MCP servers, which all write to the one service, are deployed one after another.
  dependsOn: [
    agentMcpExample
  ]
}

// The Estate health-check agent's MCP server over the app's agent API, with the two app registrations that keep it narrow
// (infra/smoke-test-estate-health-check.ps1). Like the others, it has no outputs.
module healthCheckMcp 'estate/health-check-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    apiManagementClientId: identities.outputs.apiManagementClientId
    healthCheckBaseUrl: healthCheckBaseUrl
  }
  dependsOn: [
    integrationOperationsMcp
  ]
}

// The FinOps agent's MCP server over the cost tools of the same agent API, with an agent view of its own (#511). Like the others, it has no outputs.
module healthCheckCostMcp 'estate/health-check-cost-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementClientId: identities.outputs.apiManagementClientId
  }
  dependsOn: [
    healthCheckMcp
  ]
}

// The Customer service agent's API Management front over the TMS API's agent view, with its MCP server and the two app registrations that
// keep it narrow (infra/smoke-test-customer-service-agent.ps1). Like the others, it has no outputs.
module customerServiceMcp 'estate/customer-service-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    apiManagementClientId: identities.outputs.apiManagementClientId
    tmsBaseUrl: tmsBaseUrl
  }
  dependsOn: [
    healthCheckMcp
  ]
}

// The Customer service reply agent's MCP server (one tool, the Shipper view) and the agent view app registration of its own. Its role on the
// TMS agent API is the second role of the registration customerServiceMcp makes.
module customerServiceReplyMcp 'estate/customer-service-reply-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementClientId: identities.outputs.apiManagementClientId
    tmsAgentApiAppId: customerServiceMcp.outputs.tmsAgentApiAppId
    tmsBaseUrl: tmsBaseUrl
  }
}

// The Planner agent's MCP server (one tool, the assignment options) and the agent view app registration of its own. Its role on the
// TMS agent API is the third role of the registration customerServiceMcp makes.
module plannerMcp 'estate/planner-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementClientId: identities.outputs.apiManagementClientId
    tmsAgentApiAppId: customerServiceMcp.outputs.tmsAgentApiAppId
    tmsBaseUrl: tmsBaseUrl
  }
}

// The Planner notification agent's MCP server (two tools, the Notify Shipper template and the Shipper view) and the agent view app
// registration of its own. Its role on the TMS agent API is the fourth role of the registration customerServiceMcp makes.
module plannerNotificationMcp 'estate/planner-notification-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementClientId: identities.outputs.apiManagementClientId
    tmsAgentApiAppId: customerServiceMcp.outputs.tmsAgentApiAppId
    tmsBaseUrl: tmsBaseUrl
  }
}

// The Carrier invoice matching agent's API Management front over the ERP API's Finance agent view, with its MCP server and the two app
// registrations that keep it narrow (infra/smoke-test-finance-agent.ps1). Like the others, it has no outputs.
module financeMatchingMcp 'estate/finance-matching-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    apiManagementClientId: identities.outputs.apiManagementClientId
    erpBaseUrl: erpBaseUrl
  }
  dependsOn: [
    customerServiceReplyMcp
  ]
}

// The Carrier invoice rejection reason agent's MCP server (one tool, the Carrier-visible facts of a line) and the agent view app registration of
// its own. Its role on the ERP agent API is the second role of the registration financeMatchingMcp makes. The boundary is the tool: the
// reason leaves Norvia, so that Agent's identity holds no role on the Finance agent view.
module financeRejectionReasonMcp 'estate/finance-rejection-reason-mcp.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementClientId: identities.outputs.apiManagementClientId
    erpAgentApiAppId: financeMatchingMcp.outputs.erpAgentApiAppId
    erpBaseUrl: erpBaseUrl
  }
}

// The Intake agent's Booking draft in Azure (#459): the `booking-drafts` operation of the Partner API, the host's identity and API app registration, and
// the host as a backend. Like the other agent modules it has no outputs of the template bar the identity's principal; the scripts and the AppHost find
// the app registration and the identity by their names. It writes to API Management after the MCP servers, one after another.
module bookingDraft 'estate/booking-draft.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    apiManagementName: partnerApi.outputs.apiManagementName
    apiManagementPrincipalId: identities.outputs.apiManagementPrincipalId
    bookingDraftBaseUrl: bookingDraftBaseUrl
  }
  dependsOn: [
    financeRejectionReasonMcp
  ]
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
