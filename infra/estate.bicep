// The AIS estate (ADR 0004). azd's preprovision hook deploys it before Aspire provisions the apps into the same
// resource group; the apps reference what it owns instead of creating it.
targetScope = 'subscription'

@description('The azd environment. The estate and the apps share the resource group rg-<environmentName>.')
@minLength(1)
@maxLength(64)
param environmentName string

@description('The Azure region for every resource in the estate.')
param location string

@description('''How many days the integration layer keeps what it traced: Application Insights and Log Analytics now, and
Logic Apps run history and the transit containers once they exist. The one place retention is set (coding guidelines
§7). Application Insights accepts only these values.''')
@allowed([30, 60, 90, 120, 180, 270, 365, 550, 730])
param integrationRetentionDays int = 90

@description('''The presenter's Entra object ID, empty for none. The presenter may read the estate's messages, for example to see
an event land on its topic.''')
param presenterPrincipalId string = ''

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
  }
}

// The hook stores each output as AZURE_<NAME>, which is where azd looks up the AppHost's Estate parameters.
output ESTATE_SERVICE_BUS_NAMESPACE string = messaging.outputs.namespaceName
output ESTATE_LOG_ANALYTICS_WORKSPACE string = monitoring.outputs.workspaceName
output ESTATE_APPLICATION_INSIGHTS string = monitoring.outputs.applicationInsightsName
output ESTATE_ENTRA_ISSUER string = identities.outputs.entraIssuer
output ESTATE_TMS_API_CLIENT_ID string = identities.outputs.tmsApiClientId
output ESTATE_INTAKE_CLIENT_ID string = identities.outputs.intakeClientId
output ESTATE_INTAKE_IDENTITY_ID string = identities.outputs.intakeIdentityId
