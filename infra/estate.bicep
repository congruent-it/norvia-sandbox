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

@description('''The demo UIs' host names (the TMS UI and the Partner portal), comma separated, empty before the apps are first
deployed. azd's postdeploy hook stores them, so their app registration keeps its redirect URIs when the estate is deployed
again.''')
param demoUiHosts string = ''

@description('''The TMS API's address, empty before the apps are first deployed. azd's postdeploy hook stores it, so the intake
Logic App keeps it when the estate is deployed again.''')
param tmsBaseUrl string = ''

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
  }
}

module messaging 'estate/messaging.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    presenterPrincipalId: presenterPrincipalId
  }
}

module identities 'estate/identities.bicep' = {
  scope: resourceGroup
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    presenterPrincipalId: presenterPrincipalId
    demoUiHosts: demoUiHosts
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
  }
}

module partnerApi 'estate/partner-api.bicep' = {
  scope: resourceGroup
  params: {
    location: location
    tags: tags
    apiManagementIdentityName: identities.outputs.apiManagementIdentityName
    partnerApiClientId: identities.outputs.partnerApiClientId
    shippersByClientId: join(filter(identities.outputs.shipperByClientId, entry => !empty(entry)), ';')
    personaClientIds: identities.outputs.personaClientIds
    intakeLogicAppHostName: intakeLogicApp.outputs.logicAppHostName
    intakeTriggerAudience: identities.outputs.intakeTriggerAudience
    applicationInsightsName: monitoring.outputs.applicationInsightsName
    partnerPortalPrincipalId: identities.outputs.partnerPortalPrincipalId
  }
}

// The hook stores each output as AZURE_<NAME>, which is where azd looks up the AppHost's Estate parameters.
output ESTATE_SERVICE_BUS_NAMESPACE string = messaging.outputs.namespaceName
output ESTATE_LOG_ANALYTICS_WORKSPACE string = monitoring.outputs.workspaceName
output ESTATE_APPLICATION_INSIGHTS string = monitoring.outputs.applicationInsightsName
output ESTATE_ENTRA_ISSUER string = identities.outputs.entraIssuer
output ESTATE_ENTRA_TENANT_ID string = identities.outputs.entraTenantId
output ESTATE_TMS_API_CLIENT_ID string = identities.outputs.tmsApiClientId
output ESTATE_INTAKE_CLIENT_ID string = identities.outputs.intakeClientId
output ESTATE_INTAKE_IDENTITY_ID string = identities.outputs.intakeIdentityId
output ESTATE_INTAKE_LOGIC_APP string = intakeLogicApp.outputs.logicAppName
output ESTATE_API_MANAGEMENT string = partnerApi.outputs.apiManagementName
output ESTATE_PARTNER_API_URL string = partnerApi.outputs.partnerApiUrl
output ESTATE_PARTNER_API_CLIENT_ID string = identities.outputs.partnerApiClientId
output ESTATE_PERSONA_CLIENT_IDS string = join(identities.outputs.personaClientIds, ';')
output ESTATE_PARTNER_PORTAL_IDENTITY_ID string = identities.outputs.partnerPortalIdentityId
output ESTATE_PARTNER_PORTAL_CLIENT_ID string = identities.outputs.partnerPortalClientId
output ESTATE_PARTNER_PORTAL_IDENTITY_NAME string = identities.outputs.partnerPortalIdentityName
output ESTATE_PARTNER_PORTAL_KEY_VAULT string = partnerApi.outputs.partnerPortalKeyVaultName
output ESTATE_DEMO_UIS_CLIENT_ID string = identities.outputs.demoUisClientId
