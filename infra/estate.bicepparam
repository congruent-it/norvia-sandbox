using 'estate.bicep'

// azd sets these for the environment it is provisioning.
param environmentName = readEnvironmentVariable('AZURE_ENV_NAME')
param location = readEnvironmentVariable('AZURE_LOCATION')

// The hook sets this to the signed-in Azure CLI user.
param presenterPrincipalId = readEnvironmentVariable('ESTATE_PRESENTER_PRINCIPAL_ID', '')

// The hook makes this once and keeps it, so the Carrier endpoints' credentials stay the same when the estate is deployed again.
param carrierEndpointKeySeed = readEnvironmentVariable('ESTATE_CARRIER_ENDPOINT_KEY_SEED')

// The postdeploy hook stores the TMS API's address once Aspire has deployed it.
param tmsBaseUrl = readEnvironmentVariable('ESTATE_TMS_BASE_URL', '')

// The postdeploy hook stores the Partner portal's address once Aspire has deployed it.
param partnerPortalBaseUrl = readEnvironmentVariable('ESTATE_PARTNER_PORTAL_BASE_URL', '')

// The postdeploy hook stores the Driver app API's address once Aspire has deployed it.
param driverAppBaseUrl = readEnvironmentVariable('ESTATE_DRIVER_APP_BASE_URL', '')

// The postdeploy hook stores the demo UIs' sign-in callbacks once Aspire has deployed them.
param demoUiRedirectUris = readEnvironmentVariable('ESTATE_DEMO_UI_REDIRECT_URIS', '')

// The postdeploy hook stores the Integration console's sign-in callback once Aspire has deployed it.
param integrationConsoleRedirectUris = readEnvironmentVariable('ESTATE_INTEGRATION_CONSOLE_REDIRECT_URIS', '')

// The postdeploy hook stores the Driver app's sign-in callback once Aspire has deployed it.
param driverAppRedirectUris = readEnvironmentVariable('ESTATE_DRIVER_APP_REDIRECT_URIS', '')

// The postdeploy hook stores the Simulator's sign-in callback once Aspire has deployed it.
param simulatorRedirectUris = readEnvironmentVariable('ESTATE_SIMULATOR_REDIRECT_URIS', '')

// The postdeploy hook stores the ERP's sign-in callback once Aspire has deployed it.
param erpRedirectUris = readEnvironmentVariable('ESTATE_ERP_REDIRECT_URIS', '')

// The Simulator is on in a deployed estate unless the environment says otherwise, with caps lower than its own.
param simulatorEnabled = toLower(readEnvironmentVariable('ESTATE_SIMULATOR_ENABLED_SETTING', 'true')) == 'true'
param simulatorMaxOrdersPerRun = int(readEnvironmentVariable('ESTATE_SIMULATOR_MAX_ORDERS_PER_RUN_SETTING', '200'))
param simulatorMaxConcurrentRequests = int(readEnvironmentVariable('ESTATE_SIMULATOR_MAX_CONCURRENT_REQUESTS_SETTING', '4'))
